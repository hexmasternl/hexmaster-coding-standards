# Design — Find documents by tag

## Context

The document tool surface is being built one tool at a time: `docs-serve-document-by-id` establishes `mcp-document-tools` with retrieval by id, `docs-serve-document-list` adds the full listing, and this change adds tag selection. All three are in flight and none is archived, so the shape they share matters more than usual — a client sees them as one surface, and three tools that disagree about ordering, error reporting, or payload shape are worse than three tools that agree.

What is already settled and inherited here:

- Tools project and format; the document service owns document logic. A tool with filtering logic inside it cannot be tested without a host, which is the boundary the `Docs`/`Mcp` split exists to protect.
- Failures reach the client as tool results flagged `isError`, not protocol errors, so the model sees them and can react.
- An empty result is a success; a catalog that has never loaded is a failure. "No documents" and "I could not tell you" must never look the same.
- The catalog is the authoritative metadata source, held in memory and refreshed on its own window, and `tags` is already a required catalog property: an array of zero or more lowercase kebab-case strings.

Tag selection needs nothing that is not already there. It touches no bodies, makes no GitHub request, and adds no cache.

## Goals / Non-Goals

**Goals:**

- One MCP tool taking a single tag and returning matching documents as `id`, `title`, `description`, `category`.
- Precise queries stay precise; approximate queries still get an answer.
- The client can tell an exact result from a fallback result.
- Selection logic lives in the document service and is unit-testable with no host, no network, and no HTTP handler.
- Ordering, error reporting, and payload conventions consistent with the sibling tools.

**Non-Goals:**

- Multiple tags, boolean combination, or filtering by category or status.
- Full-text search over titles, descriptions, or bodies. That is the keyword-search capability, and it is a separate problem with an unresolved question of its own.
- A tag-vocabulary or tag-frequency tool. Tag values are discoverable from the list tool's payload today.
- Fuzzy matching beyond substring — no edit distance, stemming, or synonyms.
- Any index, cache, or precomputed structure.

## Decisions

### Exact whole-tag match, with a substring fallback only when exact finds nothing

The input is trimmed and lowercased, then run in two passes:

1. **Exact.** Return every document having a tag equal to the normalised input, compared ordinally against the tag's own lowercased value.
2. **Fallback**, run only when pass 1 returns nothing. Return every document having a tag that *contains* the normalised input.

The two-pass shape is what makes this work. Substring matching alone is unusable at the short end: `ci` would drag in `cicd` and `specificity`, and the queries where an agent is least sure are exactly the short ones. Exact matching alone is brittle at the other end: an agent that reasonably asks for `testing` gets nothing when the author wrote `unit-testing`, and has no way to learn that from the empty result. Running the fallback only on an empty exact result means a query that hits a real tag is never diluted by near-misses — `ci` returns the `ci` documents and stops, and never reaches the substring pass at all.

*Alternatives considered.* Merging both passes and ranking exact above substring returns a single list where the tail is noise the agent must judge; the two-tier result is harder to explain than two-tier behaviour. Prefix matching instead of substring would miss `unit-testing` for `testing`, which is the motivating case. Edit distance would match typos but also unrelated short tags, and needs a threshold nobody can defend.

### The fallback requires at least two characters

A single-character input runs the exact pass only. A one-character substring matches almost every kebab-case tag, so the fallback would return the entire catalog dressed up as a result — the worst possible answer, because it looks like a successful narrowing. Below two characters there is no signal to work with, and an empty result is the honest answer.

### The response says when the fallback was used

The payload is four fields per entry and gains no fifth to mark match quality. Instead the tool's response text states whether the results are exact matches for the tag or approximate matches found because no document carries it exactly, naming the tag either way.

An agent that cannot distinguish these will treat an approximate hit as authoritative — it asked for `testing` and got documents, so it concludes `testing` is a tag. Saying it once at the response level costs nothing and keeps every entry the same shape as the request specified. It also gives the agent the cue to call the list tool if it wants the real tag vocabulary.

### Selection lives in the document service

The service gains a member taking a tag and returning ordered index entries; the tool normalises nothing, compares nothing, and sorts nothing. It validates that a tag was supplied, calls the service, projects four fields, and formats the response.

This is the same boundary the other two tools respect, and it is what puts normalisation, the two passes, the two-character rule, and ordering into offline unit tests over a fixture catalog — no HTTP handler is even needed, because selection never touches the network.

### Ordering by category, then id

Identical to the list tool. Two calls over the same cached catalog return identical payloads, so a client can diff or cache them, and results from the two tools sort the same way. Catalog order is not stable enough to rely on and match order would leak implementation details of the two passes.

### An in-memory scan, deliberately

Selection scans the cached catalog's entries. At tens of documents with a handful of tags each, a tag index would be infrastructure serving a problem this project does not have — and it would need invalidating on every catalog refresh. The scan sits behind the service interface, so if the catalog ever grows an index is a local change.

### Failure and empty-result behaviour follows the surface

- **Blank or whitespace-only tag** — tool error stating a tag is required. No scan.
- **No match in either pass** — empty list, success. Nothing is wrong; nothing is tagged that way.
- **Catalog never loaded** — tool error identifying the catalog as unavailable, never an empty list.
- **Cached catalog past its window with a failed refresh** — success over the cached catalog, consistent with every other read.
- **Invalid catalog entries** — already rejected by the service, so they are not scanned and cannot match.

## Risks / Trade-offs

- **The fallback can mislead.** An agent asking for `testing` and receiving `unit-testing` documents may conclude `testing` is a real tag and reuse it. → The response says the match was approximate and names the tag; the list tool exposes the actual vocabulary. The cost of the alternative — an empty result for a reasonable query — is worse.
- **Two queries differing by one character can behave categorically differently.** Adding a document tagged `test` silently switches a `test` query from fallback to exact and shrinks its results. → Correct behaviour, but surprising if unexplained; the response's exact-versus-approximate wording is what makes the shift visible rather than mysterious.
- **The payload omits `tags`.** A result cannot show what else a matching document is tagged with, so an agent cannot refine a query from the response and must call the list tool. This also makes the two tools' payloads different shapes for the same underlying entries. → Explicitly requested as four fields; the service still carries `tags`, so adding the field is additive and breaks no client.
- **Tag quality is authoring quality.** Selection is only as good as the tags in `index.json`; an untagged document is invisible to this tool no matter how relevant. → The `docs-index` skill derives tags from document content when a document is written or changed, which is where the leverage actually is.
- **Three in-flight changes edit `mcp-document-tools`.** This change, `docs-serve-document-list`, and `docs-serve-document-by-id` all add to the same spec, and the first two also touch `document-service`. → Sequencing is recorded in the proposal: the by-id change is the base, and whichever of list and tag archives second rebases onto the archived spec. Nothing here conflicts in substance — the additions are disjoint requirements.
