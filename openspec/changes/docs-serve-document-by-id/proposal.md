# Serve a document by id

## Why

The MCP server has no way to hand a client the text of a coding standard. `mcp-server-project-structure` deliberately deferred every tool ("no MCP tools exposing the document service yet"), so today the server completes protocol initialization and offers an empty tool list — it is a host with nothing to serve. Retrieval by id is the smallest useful capability and the one every later tool (index, search) composes on top of, so it goes first.

While building it, the body-loading strategy changes: instead of downloading the whole repository archive to have every body in memory, the server fetches a body from the GitHub API only when a client first asks for it, and remembers it for 30 minutes.

## What Changes

- Add an MCP tool that takes a catalog `id` and returns that document's metadata and full markdown body.
- The tool reports an explicit, id-naming error when no catalog entry has that id, or when the entry resolves to a file GitHub does not have. Not-found is never an empty success.
- **BREAKING** (against the not-yet-implemented `document-service` spec, not against shipped behaviour): document **bodies** are no longer taken from the repository archive. The catalog is still loaded and refreshed as a whole; each body is fetched individually from the GitHub Contents API on first request.
- Add a per-document in-memory body cache with a 30-minute expiry. A hit inside the window serves with no network call; a miss fetches, caches, and serves. The interval is configurable.
- Drop the archive download and its `docs/`-prefix extraction hardening for bodies, replacing that attack surface with path handling on the catalog's `path` values before they are put into a GitHub API URL.
- Update `CLAUDE.md`, whose "Architecture notes" currently state the opposite ("Do not replace it with per-document fetches").

## Capabilities

### New Capabilities
- `mcp-document-tools`: The MCP tool surface for documents — the retrieve-by-id tool's name, description, input schema, success payload, and how it reports not-found and upstream failure to an MCP client.

### Modified Capabilities
- `document-service`: Body loading moves from archive-wide extraction to per-document lazy fetch over the GitHub Contents API, with a per-document 30-minute cache. Catalog loading, refresh, and the retrieve-by-id contract's outward shape are unchanged; what changes is where a body comes from, when it is fetched, how long it is held, and how a body-level failure is reported separately from an unknown id.

> `document-service` is introduced by the in-flight `mcp-server-project-structure` change and is not yet in `openspec/specs/`. This change's delta applies on top of that one; it must be archived first.

## Impact

- **Code**: `src/HexMaster.CodingStandards.Docs` — the document service gains a body cache and a GitHub Contents API client, and loses archive download/extraction for bodies; `src/HexMaster.CodingStandards.Mcp/Tools/` gains its first real tool, registered in `Program.cs`.
- **Tests**: `tests/HexMaster.CodingStandards.Docs.Tests` — new coverage for cache hit/miss/expiry, unknown id, and a catalogued path GitHub cannot resolve, all against a fake HTTP handler so the suite stays offline.
- **Runtime behaviour**: cold start is cheaper (catalog only, no archive), memory grows with what clients actually read, and GitHub request volume rises from one per refresh to one per document per 30 minutes. Unauthenticated rate limits become a real consideration rather than an irrelevance; the existing optional access token covers it.
- **Freshness**: a document edited on `main` can be served stale for up to 30 minutes even after a catalog refresh, because the body cache expires independently of the catalog.
- **Health**: `GET /health` continues to key off the catalog having loaded; an unreachable body does not make a replica unhealthy.
- **Docs**: `CLAUDE.md` architecture notes on archive download, extraction confinement, and the 15-minute cache need rewriting.
- **Unresolved knock-on**: `document-service` requires keyword search to match against each document's markdown body, which assumed every body was resident in memory. Lazy fetching removes that assumption. Search is not implemented and is not in this change's scope, so the requirement is left untouched here and the conflict is recorded as an open question in `design.md` — it must be settled before search is built.
- **Non-goals**: no index tool, no search tool, no cache eviction by size, no conditional requests or ETag revalidation, no authentication on the MCP endpoint.
