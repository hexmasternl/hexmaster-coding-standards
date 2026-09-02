# Find documents by tag

## Why

Listing every catalogued document works while the catalog is small, but it puts the whole set in front of the agent on every question and makes it read descriptions to decide what is relevant. Tags are the one piece of catalog metadata that already encodes subject matter deliberately — an author chose them — and nothing exposes them as a query. An agent asking "what do we say about testing" should be able to ask the server, not filter a full listing itself.

Tag selection is also the cheapest useful narrowing the server can offer: it reads catalog metadata only, needs no document bodies, and therefore no per-document GitHub request.

## What Changes

- Add an MCP tool that takes one required tag string and returns the matching documents as `id`, `title`, `description`, and `category`.
- Matching is **exact whole-tag first, substring fallback second**: the input is trimmed and lowercased, then compared against whole tags; only if that yields nothing are documents with a tag *containing* the input returned. This keeps a precise query precise while still answering `testing` when the catalog says `unit-testing`.
- The tool states in its response when the fallback produced the results, so an agent can tell an exact hit from an approximate one without a per-entry field.
- No match in either pass returns an empty list as a success. A blank or whitespace-only tag is rejected. A catalog that has never loaded is an error, never an empty result.
- Results are ordered by `category`, then `id`, matching the list tool, so repeated calls are diffable.
- Tag selection lives in the document service, not the tool — the tool projects and formats, consistent with the existing tool boundary.

## Capabilities

### New Capabilities
<!-- None. Both affected capabilities already exist. -->

### Modified Capabilities
- `mcp-document-tools`: Adds a third tool to the document tool surface — the tag tool's name, description, single required input, four-field payload, ordering, how it signals a fallback match, and how it reports a blank tag, no matches, and an unloaded catalog.
- `document-service`: Adds tag selection to the service — input normalisation, the two-pass exact-then-substring rule, ordering, and the guarantee that selection reads catalog metadata only and fetches no document bodies.

> **Sequencing.** Neither capability is in `openspec/specs/` yet. `document-service` comes from the in-flight `mcp-server-project-structure` change and `mcp-document-tools` from the in-flight `docs-serve-document-by-id` change; both must be archived before this change's deltas apply. `docs-serve-document-list` also adds a tool to `mcp-document-tools` and changes `document-service`'s catalog acquisition — that change and this one are independent in substance but touch the same two specs, so whichever archives second rebases its delta onto the archived spec.

## Impact

- **Code**: `src/HexMaster.CodingStandards.Mcp/Tools/` gains the tag tool, registered in `Program.cs`; `src/HexMaster.CodingStandards.Docs` gains a tag-selection member on the document service interface and its implementation.
- **Tests**: `tests/HexMaster.CodingStandards.Docs.Tests` gains coverage for normalisation, exact matching, the fallback and the conditions under which it does and does not run, ordering, no-match, blank input, and an unloaded catalog — all offline, with no HTTP handler needed because selection touches no network.
- **Runtime behaviour**: no new GitHub requests and no new caching. Selection is an in-memory scan over the cached catalog, which is tens of entries; cost is negligible and no index is warranted.
- **Client-visible**: the payload omits `tags`, so a result does not show what other tags a matching document carries and an agent cannot refine a query from the response alone. It must call the list tool to discover tag values. This diverges from the list tool's five-field payload; adding the field later is additive.
- **Unaffected by lazy bodies**: `docs-serve-document-by-id` makes document bodies lazily fetched, which leaves the base spec's body-matching keyword search unresolved. Tag selection reads metadata only, so it works identically under either body strategy and does not depend on that question being settled.
- **Docs**: `README.md` gains the tool; `CLAUDE.md` needs no architecture change, since nothing about content acquisition moves.
- **Non-goals**: no multi-tag queries, no AND/OR combination, no category or status filtering, no full-text search, no tag-vocabulary or tag-frequency tool, no paging, no bodies in the response, no fuzzy or edit-distance matching beyond the substring fallback.
