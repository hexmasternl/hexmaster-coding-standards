# Design — Serve a document by id

## Context

`mcp-server-project-structure` establishes the host, the `Docs` project, the `/docs` tree, and `index.json` as the authoritative catalog. It also specifies that a refresh downloads the **repository archive** for the configured ref in one request and takes the catalog and every body from that archive, holding the whole set in memory behind a 15-minute TTL. No tool exposes any of it: that proposal explicitly deferred tools to a later change. This is that change.

Two things move at once, and they are related. The tool needs a body; the body needs to come from somewhere. Fetching bodies per id — the decision taken for this change — removes the archive, its extraction hardening, and its all-or-nothing refresh from the body path, and replaces them with a per-document cache and a per-document GitHub request. The catalog keeps its existing whole-set refresh; only bodies go lazy.

Constraints inherited and not up for negotiation here:

- The `Docs` project must not reference the host or ASP.NET Core hosting types, so everything below stays testable without a `WebApplicationFactory`.
- The test suite must pass with no network access.
- Replicas scale to zero and MCP runs stateless, so the cache is per-replica, and nothing may assume a client's second request reaches the same process.
- No secrets in the repository or in logs; the optional GitHub token stays configuration-only.

## Goals / Non-Goals

**Goals:**

- An MCP tool that returns a catalogued document's metadata and full markdown body for a given `id`.
- An explicit, id-naming error for an unknown id, and a distinct one for a catalogued document whose body GitHub will not serve. Neither is an empty success.
- A per-document in-memory body cache with a 30-minute lifetime: a hit inside the window makes no network call.
- Bodies fetched from the GitHub Contents API on a miss, honouring the configured owner, repository, ref, and optional token.
- Catalog `path` values treated as untrusted input before they become part of a URL.

**Non-Goals:**

- Index and search tools. They compose on this but are separate changes.
- Conditional requests, ETag revalidation, or any freshness mechanism finer than the fixed lifetime.
- Size- or memory-bounded eviction. Expiry is the only eviction policy.
- Sharing the cache across replicas, persisting it, or warming it at startup.
- Changing how the catalog itself is loaded or refreshed.

## Decisions

### Fetch bodies from the GitHub Contents API, one document at a time

`GET /repos/{owner}/{repo}/contents/{path}?ref={ref}` with `Accept: application/vnd.github.raw` returns the file's bytes directly, honours the configured ref, and accepts the same optional token as the rest of the client. One endpoint covers every body.

*Alternatives considered.* `raw.githubusercontent.com` avoids the API rate limit entirely but is a different host with different caching semantics and no way to send the API token, so a private fork or a rate-limited runner has no recourse. The Git Trees + Blobs API needs two round trips per document. The archive — the incumbent — is exactly what this change is replacing.

*What this costs.* Request volume goes from one per refresh to one per document per lifetime window. Unauthenticated GitHub allows 60 requests/hour/IP; with a few dozen documents and a 30-minute window, a busy replica can plausibly reach that. The token turns 60 into 5,000 and is already in the configuration surface. This is the main thing given up by leaving the archive behind, and it is called out as a risk below.

### The catalog stays whole-set and refreshed on its own interval

Only bodies go lazy. `docs/index.json` is still fetched in full and swapped atomically on the existing refresh interval (default 15 minutes), and `GET /health` still keys off "the catalog has loaded at least once". An unreachable body is a per-request failure, not a replica-level one.

Keeping these two clocks separate is deliberate: the catalog is what makes a document *visible*, and it is small enough to refresh eagerly. A body is what makes a document *readable*, and paying for it only when someone reads it is the whole point of the change. The cost is two staleness windows to reason about rather than one — see Risks.

### A purpose-built cache over `ConcurrentDictionary` plus `TimeProvider`

The cache is a small type in the `Docs` project: a `ConcurrentDictionary` keyed by the resolved content path, holding the body and the instant it was fetched. Expiry is **absolute from fetch time**, not sliding — a popular document must not be able to keep itself alive indefinitely, because bounded staleness is the property being promised.

Time comes from an injected `TimeProvider`, which is what makes "still cached at 29 minutes, refetched at 31" an ordinary offline unit test rather than a `Thread.Sleep`.

*Alternatives considered.* `IMemoryCache` gives expiry for free, but its clock is awkward to fake, it offers no single-flight, and its size-limit machinery would go unused. Caching at the `HttpClient` handler level would put the policy in the wrong layer and make the 30-minute window invisible to tests.

Keying by resolved path rather than by id means a catalog edit that repoints an id at a different file misses the cache immediately, rather than serving the old file until expiry.

### One fetch per id under concurrency

Entries are stored as a lazily-started task, so N concurrent requests for the same uncached document produce one GitHub request and N awaits of the same result. Without this, a cold replica hit by a burst multiplies its own rate-limit consumption for no benefit.

A **failed** fetch is not cached: the entry is removed so the next request retries. Caching a transient 5xx or a rate-limit rejection for 30 minutes would turn a blip into a half-hour outage for that document. The cost is that a genuinely missing file is re-requested on every call; that only happens when the catalog and the tree disagree, which CI already fails the build for.

### Catalog paths are validated before they become a URL

The catalog is downloaded from a public repository and is untrusted input, exactly as the archive was. Before a `path` is used it must be relative, free of `..` segments and backslashes, and rooted at `docs/` under one of the three category folders; each segment is then URL-encoded into the API path. A path failing this is treated as an unresolvable body and logged, and no request is made.

This is the successor to the archive's `docs/`-prefix extraction confinement. The old risk was writing outside a directory; the new one is a crafted path steering a request at another repository path or another endpoint. The check is cheap and belongs on the boundary.

### Not-found and unavailable are different failures

- **Unknown id** — no catalog entry matches. Deterministic, the caller's mistake, and fixed by asking for a different id. No network call is made.
- **Body unavailable** — the entry exists but GitHub returned 404, rate-limited the request, or failed. Transient, or an authoring bug; retrying later may work.

Both reach the MCP client as a tool error rather than a protocol error, so the model sees the failure in the tool result and can react — an MCP tool result carries `isError`, and reserving JSON-RPC errors for protocol faults is the SDK's convention. The messages differ and both name the id, because a model that cannot tell "you asked for something that does not exist" from "the server could not reach GitHub" will retry the wrong one.

### One tool class, registered at composition time

`GetDocumentTool` lands in `src/HexMaster.CodingStandards.Mcp/Tools/` and is registered in `Program.cs`, which is the seam `mcp-server-host` specifies. It depends only on the document-service interface and holds no HTTP, GitHub, or caching logic of its own — those live in `Docs`, where they can be tested.

## Risks / Trade-offs

- **GitHub rate limits become load-bearing.** Anonymous access allows 60 requests/hour/IP; per-document fetching can reach that where whole-archive refresh never could. → The optional token (already in the configuration surface, already kept out of logs) raises it to 5,000/hour. The 30-minute lifetime and single-flight cap the request rate at roughly (documents ÷ 30 min) per replica. A rate-limit rejection degrades one document, not the server.
- **Two independent staleness windows.** A document edited on `main` can be catalogued-fresh but body-stale for up to 30 minutes, and the catalog and the body may come from different commits — a property the archive approach guaranteed against. → Acceptable for coding standards, which change rarely and are not transactional; documented in `CLAUDE.md` so nobody rediscovers it as a bug. If it ever matters, keying the cache by commit SHA rather than ref closes it.
- **Cache growth is bounded only by expiry.** A client walking every id holds every body for 30 minutes. → The catalog is tens of markdown documents; the ceiling is small and predictable. Expired entries are swept on catalog refresh so an idle replica does not hold content forever. A size cap stays a non-goal until the catalog outgrows the assumption.
- **Cold reads are slower and can fail.** The first read of any document now waits on GitHub and can fail where previously every body was already in memory. → Failures are per-document and explicit, health is unaffected, and the second read inside the window is free.
- **This change contradicts committed guidance.** `CLAUDE.md` currently says the archive approach is deliberate and instructs against per-document fetches. → Rewriting those notes is a task in this change, not a follow-up; leaving them would send the next contributor to undo this.
- **The base spec is not archived yet.** `document-service` lives in the in-flight `mcp-server-project-structure` change, so this change's `MODIFIED` delta has nothing under `openspec/specs/` to apply against. → That change must be archived first; until then the two must be read together.

## Open Questions

### Keyword search over bodies no longer has bodies to search

`document-service` requires search to match "case-insensitively against each document's `title`, `description`, `tags`, and **markdown body**", ranking metadata matches above body matches. That requirement assumes every body is resident in memory — which is exactly what the archive gave it and what lazy fetching takes away. Searching bodies now means fetching every document on the first search, which would undo the change.

Neither search nor the archive is implemented yet, so nothing is broken today; this change deliberately leaves the search requirement untouched rather than quietly narrowing it. It has to be settled before search is built, and the plausible resolutions are:

- **Search metadata only.** Match `title`, `description`, and `tags`; drop body matching and the two-tier ranking with it. Cheapest, and honest about what the server can see. Loses "find the standard that mentions X".
- **Search what is cached, and say so.** Match bodies that happen to be cached, metadata for everything else. Cheap, but results depend on what other clients recently read — non-deterministic in a way that is hard to explain to a model.
- **Keep a body index the catalog refresh maintains.** Fetch every body on catalog refresh to build a search index while still serving reads from the per-document cache. Preserves full-text search at the cost of reintroducing whole-set fetching — at which point the archive is the better mechanism for it.

If full-text search turns out to be a requirement rather than a nice-to-have, that is an argument for revisiting the per-document decision made here, not for patching around it in the search change.
