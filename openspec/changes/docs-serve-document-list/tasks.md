## 1. Prerequisites

- [x] 1.1 Confirm `mcp-server-project-structure` is implemented and archived, so `document-service` and the host exist in `openspec/specs/` — implemented in code (its 3 open tasks are Azure-only verification); **not archived**, so `openspec/specs/` is still empty and this delta stacks on the in-flight change
- [x] 1.2 Confirm `docs-serve-document-by-id` is implemented and archived, so `mcp-document-tools` exists and bodies are already fetched lazily — implemented through task group 7 (its 5 open tasks are end-to-end verification and docs); **not archived**
- [x] 1.3 Rebase this change's `document-service` delta onto the archived spec: if `docs-serve-document-by-id` already removed the archive-extraction requirement, drop that removal here and keep only the catalog-fetch and cache-window edits — dropped the `REMOVED` block, restated by-id's body-fetch and body-cache text so this delta no longer reverts it, and added the listing-projection requirement
- [x] 1.4 Note the retrieve-by-id tool's actual name and description style, so the list tool matches its convention

## 2. Catalog acquisition by single file fetch

- [x] 2.1 Add a catalog fetch that requests `docs/index.json` for the configured ref in one HTTPS request, reusing the existing named `HttpClient`, timeout, retry, and optional access token
- [x] 2.2 Send no token when none is configured, and keep the token out of logs and exception messages
- [x] 2.3 Wire the fetch behind the existing content-fetching interface so it stays substitutable in tests
- [x] 2.4 Delete the archive downloader, the tarball extraction routine, and its `docs/`-prefix confinement code, along with their now-unused options and fixtures
- [x] 2.5 Remove the archive fixture-based tests, keeping any that still describe live behaviour by porting them to the single-file fetch
- [x] 2.6 Verify the solution builds with no warnings and no unreferenced archive-handling code remains

## 3. Catalog cache with a 30-minute time-to-live

- [x] 3.1 Replace the refresh-interval option with a catalog cache time-to-live defaulting to 30 minutes, keeping it environment-variable overridable
- [x] 3.2 Implement lazy expiry: evaluate the window when the catalog is requested and fetch on the first request past it
- [x] 3.3 Reduce the background refresh service to a one-shot startup load, keeping the eager first load so a replica passes its readiness probe before any request arrives, and removing the recurring timer
- [x] 3.4 Implement single-flight coordination so concurrent callers on an expired cache trigger exactly one fetch and all await its outcome
- [x] 3.5 Replace the cached catalog atomically, so no reader observes a partially loaded catalog
- [x] 3.6 Keep the failure behaviour: a failed fetch after a successful load logs and serves the stale catalog; a failed first load reports not-ready and fails callers explicitly
- [x] 3.7 Confirm `GET /health` still keys off a catalog having loaded, and stays healthy when only a later fetch has failed
- [x] 3.8 Add unit tests with a controllable clock and fake HTTP handler: cache hit inside the window, fetch on first request after expiry, no fetch while idle, concurrent callers sharing one fetch, atomic replacement, stale-on-failure, not-ready on cold-start failure, and repeated failures never clearing the cache

## 4. Index projection

- [x] 4.1 Add the projection type carrying exactly `id`, `title`, `category`, `description`, and `tags`, keeping `status` on the service's own index entry
- [x] 4.2 Project from the cached catalog only, with no document body read
- [x] 4.3 Order entries by category, then by id
- [x] 4.4 Return `tags` as an empty array, never null or omitted, when a document has no tags
- [x] 4.5 Exclude entries the catalog parser rejected as invalid, without failing the projection
- [x] 4.6 Add unit tests for the five fields and nothing more, the ordering guarantee, byte-identical repeated calls, empty tags, an empty catalog, and a catalog containing one invalid entry

## 5. MCP list tool

- [x] 5.1 Add the list tool class in `src/HexMaster.CodingStandards.Mcp/Tools/`, named to match the retrieve-by-id tool's convention
- [x] 5.2 Declare an empty input schema and ignore any unrecognised argument a client supplies
- [x] 5.3 Write the tool description so an agent can distinguish it from search without calling it: all documents, metadata only, no bodies
- [x] 5.4 Return the projection as the tool result, and register the tool in `Program.cs`
- [x] 5.5 Map an empty catalog to an empty successful list, and a never-loaded catalog to a tool error naming the catalog as unavailable
- [x] 5.6 Return the cached listing successfully when the catalog is stale after a failed fetch
- [ ] 5.7 Verify with a real MCP client that the tool appears in the tool list, is callable with no arguments, and returns the expected payload

## 6. Documentation and close-out

- [x] 6.1 Rewrite the `CLAUDE.md` architecture notes: single `index.json` fetch instead of an archive download, 30-minute lazy cache instead of a 15-minute background refresh, and remove the "do not replace it with per-document fetches" and extraction-confinement notes
- [x] 6.2 Document the available MCP tools and the 30-minute freshness window in the root `README.md`
- [ ] 6.3 Resolve the open question on `status`: either add it to the payload or filter non-current entries, or record the decision to ship without it
- [ ] 6.4 Confirm a full green run: `dotnet build`, `dotnet test`, catalog validation, `docker build`, and a successful CD run
