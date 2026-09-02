# Serve the list of available documents

## Why

A client that can only fetch a document by id has to already know the id. Nothing in the server tells it what exists, so the retrieve-by-id tool is unusable on its own — an agent would have to guess ids. Listing the catalog is the discovery step that makes every other document tool reachable, and it is cheap: the metadata a client needs to choose a document is already sitting in `index.json`, so the listing needs no document bodies and no per-document requests.

## What Changes

- Add an MCP tool that takes no arguments and returns every catalogued document as `id`, `title`, `category`, `description`, and `tags`.
- The listing is projected from `index.json` alone. No document body is read, and no per-document GitHub request is made to satisfy it.
- Results are returned in a stable order (category, then id) so repeated calls are diffable and cacheable by the client.
- An empty catalog returns an empty list as a success. A catalog that has never loaded is reported as a failure, never as "no documents".
- **`index.json` is fetched as a single file and cached for 30 minutes**, replacing the repository-archive download and the 15-minute refresh interval that `mcp-server-project-structure` specified. This aligns the catalog's cache window with the per-document body cache that `docs-serve-document-by-id` introduces, so the whole service has one freshness story instead of two.
- Update `CLAUDE.md`, whose architecture notes still describe a single archive download and a 15-minute cache.

## Capabilities

### New Capabilities
<!-- None. The MCP tool surface and the document service both already exist as capabilities. -->

### Modified Capabilities
- `mcp-document-tools`: Adds a second tool to the document tool surface — the list tool's name, description, empty input schema, payload shape, ordering guarantee, and how it reports a catalog that has never loaded.
- `document-service`: Catalog acquisition moves from extracting `index.json` out of a downloaded repository archive to fetching that one file directly from GitHub, and the catalog cache window becomes 30 minutes rather than a 15-minute background refresh interval, expiring lazily on request. The service's own index keeps carrying `status`; it is the tool's payload that projects five fields.

> **Sequencing.** Neither capability is in `openspec/specs/` yet: `document-service` comes from the in-flight `mcp-server-project-structure` change, and `mcp-document-tools` from the in-flight `docs-serve-document-by-id` change. Both must be archived before this change's deltas apply. This change and `docs-serve-document-by-id` both touch `document-service`'s download requirements, so whichever is archived second must rebase its delta onto the archived spec — see the design's sequencing note.

## Impact

- **Code**: `src/HexMaster.CodingStandards.Mcp/Tools/` gains the list tool, registered in `Program.cs`; `src/HexMaster.CodingStandards.Docs` swaps the archive downloader for a single-file catalog fetch and moves the catalog cache to a 30-minute window.
- **Deleted code**: the archive download and its `docs/`-prefix extraction hardening lose their last consumer once bodies are fetched lazily, so they come out rather than sitting unused.
- **Tests**: `tests/HexMaster.CodingStandards.Docs.Tests` gains coverage for the projection's five fields, the ordering guarantee, an empty catalog, a never-loaded catalog, and cache hit/miss/expiry at 30 minutes — all against a fake HTTP handler so the suite stays offline.
- **Runtime behaviour**: cold start drops to one small file fetch instead of a repository archive, so the scale-from-zero penalty shrinks. Catalog freshness worsens from 15 to 30 minutes, by choice.
- **Client-visible**: the payload deliberately omits each entry's `status`, so a client cannot tell an `accepted` standard from a `superseded` or `deprecated` one. Flagged in the design; the service still carries the field, so adding it later is additive.
- **Docs**: `CLAUDE.md` architecture notes on the archive download, extraction confinement, and the 15-minute cache need rewriting.
- **Non-goals**: no filtering or paging on the listing (no category or tag parameters), no search, no bodies in the response, no conditional requests or ETag revalidation on the catalog fetch, no cache eviction by size, no authentication on the MCP endpoint.
