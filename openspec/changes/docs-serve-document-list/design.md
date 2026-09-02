# Design: serve the list of available documents

## Context

`mcp-server-project-structure` builds the skeleton: an `Mcp` host serving the protocol, a `Docs` project owning content, and a document service with retrieval, an index, and keyword search — but it deliberately ships no MCP tools, so the server initializes and offers an empty tool list. `docs-serve-document-by-id` adds the first tool and changes body loading to a lazy per-document fetch cached for 30 minutes.

Both are in-flight. Nothing is implemented: `openspec/specs/` is empty and `mcp-server-project-structure`'s task list is untouched.

This change adds the discovery half. Everything the listing needs is already in `index.json` — id, title, category, description, tags — so the tool is a projection over the cached catalog with no new data source and no body reads. What makes the change more than a thin adapter is that it settles how the catalog itself is acquired and how long it is held, which the two in-flight changes leave inconsistent.

## Goals / Non-Goals

**Goals:**

- A tool an agent can call with no arguments to learn what standards exist, returning enough metadata to pick one without fetching it.
- A catalog acquisition story that is the same shape as body acquisition: one file, fetched on demand, cached for 30 minutes.
- Retire the repository-archive download and its extraction hardening rather than leave dead code with a security-shaped surface.
- A stable, deterministic response order.
- Offline, fixture-driven tests for the projection, ordering, and cache behaviour.

**Non-Goals:**

- Filtering, paging, or sorting parameters on the tool. It lists everything; narrowing is what the search tool is for.
- Document bodies in the response.
- Conditional requests (ETag / `If-None-Match`) on the catalog fetch, or cache eviction by size.
- Changing how bodies are fetched or cached — that is `docs-serve-document-by-id`'s decision, and this change leaves it alone.

## Decisions

### The catalog is one file fetch, not an archive extraction

Catalog acquisition becomes a single HTTPS GET of `docs/index.json` at the configured ref, cached in memory for 30 minutes.

*Why:* the archive download in `mcp-server-project-structure` bought exactly one thing — the catalog and every body from the same commit — and `docs-serve-document-by-id` gives that up when it moves bodies to lazy per-document fetches. With bodies fetched individually, downloading a whole repository archive to read one JSON file out of it is all cost and no benefit: a much larger transfer on every cold start, tarball handling, and an extraction routine that has to defend against traversal entries because a public repository's archive is untrusted input. Fetching the one file removes the transfer, the tarball code, and that entire attack surface.

*Consequence:* the catalog and the bodies can now come from different commits — a document listed by a catalog read at one moment can have a body fetched from a later commit. For markdown standards this is invisible in practice; the failure it enables is a listed id whose body 404s, which the by-id tool already has to report as a distinct upstream failure.

*Alternative considered:* keeping the archive purely for the catalog. Rejected — it keeps the extraction hardening alive to serve a single file, which is the worst of both.

### 30 minutes, and the same 30 minutes as bodies

The catalog cache window is 30 minutes, replacing the 15-minute refresh interval, and it is the same configurable value that governs body caching.

*Why:* two different windows produce a confusing freshness story — "the list updates in 15 minutes but the document you open can be half an hour old" — and there is no reason for the catalog to be fresher than the content it describes. One window makes the guarantee statable in a sentence: nothing you see is more than 30 minutes old.

*Trade-off:* a newly published standard takes up to 30 minutes to appear in a listing, up from 15. Accepted deliberately — standards change on a scale of days, and the cost of the shorter window is paid on every cold start.

### The cache is time-to-live, not a background refresh

The catalog is fetched lazily: the first request after the window expires triggers the fetch, rather than a background service refreshing on a timer.

*Why:* under scale-to-zero a background timer mostly refreshes content nobody is asking for, and a replica that lives for one request would fetch twice. Lazy expiry ties the request volume to actual use. It also removes the background service, whose failure mode (silent, and only visible in logs) is worse than a failure the requesting caller sees.

*Consequence:* the request that finds the cache expired pays the fetch latency. One small file over HTTPS, so this is tens of milliseconds, not the seconds an archive download cost.

*Concurrency:* concurrent callers arriving on an expired cache must not each fire a fetch. One fetch runs and the others await its result; if it fails and a previously loaded catalog exists, they are all served the stale catalog.

### A failed refresh serves stale; a never-loaded catalog is a failure

Unchanged in spirit from `mcp-server-project-structure`, and worth restating because it is what keeps the tool honest: once a catalog has loaded, an expired window whose fetch fails serves the last good catalog and logs. Only a replica that has never loaded a catalog fails the call, and it fails explicitly rather than returning an empty list.

*Why:* "no documents" and "I cannot reach GitHub" mean completely different things to an agent. Collapsing them into an empty array would have it conclude the standards repository is empty and stop asking.

### The tool takes no arguments and returns five fields

Tool name `list_documents`, empty input schema, returning an array of `{ id, title, category, description, tags }` ordered by category then id.

*Why no arguments:* an agent calling this is orienting itself. Filter parameters would invite it to guess a category or tag string, get an empty result, and conclude nothing exists — whereas the full listing is small enough (tens of entries) to hand over whole and lets the agent filter on content it can see. Narrowing by keyword is the search tool's job.

*Why the ordering guarantee:* grouping by category is how a reader wants to see standards, and a deterministic order means a client can cache or diff the response instead of treating each call as unrelated.

*Why `description` is in the payload:* it is the field that lets an agent choose without fetching. Returning ids and titles alone would force a fetch-to-decide loop, which is the cost the listing exists to avoid.

The tool's name and description must follow whatever convention `docs-serve-document-by-id` establishes for its retrieve tool; a `get_document` / `list_documents` pair is assumed. The tool description is part of the contract, not decoration — it is what an agent reads to decide whether to call this instead of search, so it must say that this returns all documents with metadata and no bodies.

### `status` is omitted from the payload, as specified

The five fields are what was asked for, and `status` is not among them.

This is worth flagging rather than burying: catalog entries carry `draft`, `accepted`, `superseded`, and `deprecated`, and a listing that omits it presents a superseded standard as indistinguishable from a current one. An agent choosing what to follow will pick wrong, silently, and a deprecated standard is arguably worse than no standard.

Built as specified. Two mitigations are one-line changes whenever wanted, because the service still carries the field: add `status` to the projection, or filter non-current entries out of the listing by default. Recorded as an open question rather than decided unilaterally.

## Risks / Trade-offs

- **A listing without `status` can send an agent to a superseded standard** → No mitigation in this change, by specification. The field survives in the service, so adding it to the projection or filtering on it is additive. Raised as the first open question.
- **Catalog and bodies can come from different commits** → Accepted; it is the direct consequence of dropping the archive, and the concrete failure (a listed id whose body 404s) is already a case the by-id tool must report distinctly.
- **Freshness halves, from 15 to 30 minutes** → Chosen, for one coherent freshness window. If a shorter one is wanted later it is a configuration value, not a redesign.
- **The request that finds the cache expired pays the fetch** → Small file, so tens of milliseconds. Single-flight coordination keeps a burst of concurrent callers from multiplying the cost.
- **Removing extraction hardening looks like removing a security control** → It removes the *input* that made the control necessary. Nothing else consumes the archive once bodies are lazy; leaving unused tarball-extraction code in the tree is the worse outcome. The path handling that remains — validating catalog `path` values before they become GitHub URLs — belongs to `docs-serve-document-by-id`.
- **Request volume rises with use rather than being fixed** → One catalog fetch per replica per 30 minutes is negligible; the volume concern lives with per-document body fetches, and the existing optional access token covers rate limits.
- **This change and `docs-serve-document-by-id` both modify `document-service`'s download requirements** → See sequencing below. The real risk is archiving them out of order and silently losing one delta.

## Sequencing

Three changes touch the same capability and must land in order:

1. `mcp-server-project-structure` — creates `document-service` and the host.
2. `docs-serve-document-by-id` — creates `mcp-document-tools`, moves bodies to lazy fetch.
3. This change — adds the list tool, moves catalog acquisition to a single file fetch on a 30-minute window.

Both this change and `docs-serve-document-by-id` carry a `document-service` delta against requirements that only exist in change 1. Whichever is archived second must be rebased onto the then-current spec before archiving, because a MODIFIED requirement carries its full replacement text and a stale copy would quietly revert the other change's edit. Concretely: if `docs-serve-document-by-id` lands first and removes the archive-extraction requirement, this change's delta drops its own removal and keeps only the catalog-fetch and cache-window edits.

Merging the two changes into one is a reasonable alternative if they are going to be implemented together anyway — it removes the rebase entirely. Kept separate here because they are independently useful and independently reviewable.

## Open Questions

- **Should the listing carry `status`, or filter on it?** Omitted as specified, but a client that cannot see `superseded` will follow superseded standards. Adding the field, or excluding non-current entries by default, are both small changes — this needs a decision before the tool is relied on.
- **Tool naming convention** — `list_documents` assumes the by-id change names its tool `get_document`. Whichever convention that change sets, both tools should share it.
- **Should the 30-minute window be one setting or two?** Treated as one value governing catalog and bodies alike. Splitting them is easy but reintroduces the two-freshness-story problem this change set out to remove.
- **Is a manual refresh path wanted** — a tool or endpoint that invalidates the cache so a just-merged standard appears immediately, rather than waiting out the window.
