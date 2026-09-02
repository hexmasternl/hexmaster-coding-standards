# Tasks — Find documents by tag

> Prerequisite: `mcp-server-project-structure` (creates the `Docs` project and the document service) and `docs-serve-document-by-id` (creates the `mcp-document-tools` surface) must be implemented and archived first. If `docs-serve-document-list` archives before this change, rebase these deltas onto the archived specs.

## 1. Service contract

- [x] 1.1 Define the tag selection result type so a caller can tell an exact match from a fallback match, and an unloaded catalog from an empty result, without parsing a message string
- [x] 1.2 Add a tag selection member to the document service interface taking a tag string and returning ordered index entries wrapped in that result type
- [x] 1.3 Confirm the index entry type already carries `id`, `title`, `description`, and `category`, and reuse it rather than introducing a second entry shape

## 2. Normalisation and matching

- [x] 2.1 Normalise the supplied tag by trimming whitespace and lowercasing, and reject a tag that is empty or whitespace-only before any scan
- [x] 2.2 Implement the exact pass: return every document having a tag whose lowercased value equals the normalised input, compared ordinally
- [x] 2.3 Implement the fallback pass, run only when the exact pass returns nothing and the normalised tag is at least two characters, returning documents having a tag containing the normalised input
- [x] 2.4 Ensure the passes are never merged: when the exact pass matches at least one document, the fallback neither runs nor contributes
- [x] 2.5 De-duplicate so a document carrying several satisfying tags is returned exactly once
- [x] 2.6 Skip entries the service rejected as invalid, so they can neither match nor fail the selection
- [x] 2.7 Order results by `category`, then `id`, independent of catalog order and of the order matches were found
- [x] 2.8 Return a failure when no catalog has ever loaded, and select over the cached catalog when a refresh has failed but a catalog is cached

## 3. Service tests

- [x] 3.1 Test normalisation: surrounding whitespace ignored, case ignored, blank and whitespace-only rejected without returning the catalog
- [x] 3.2 Test the exact pass excludes near misses — `ci` returns the `ci` document and not the `cicd` one
- [x] 3.3 Test the fallback returns `unit-testing` for `testing` and reports the match as a fallback
- [x] 3.4 Test a one-character tag matches exactly but never triggers the fallback
- [x] 3.5 Test a document carrying two satisfying tags is returned once, and that no match returns an empty success
- [x] 3.6 Test ordering is by category then id and is identical across two selections over the same catalog
- [x] 3.7 Test an invalid catalog entry cannot match, an unloaded catalog fails, and a stale cached catalog succeeds
- [x] 3.8 Confirm every test above runs over a fixture catalog with no HTTP handler, no host, and no network

## 4. The MCP tool

- [x] 4.1 Add the tag tool to `src/HexMaster.CodingStandards.Mcp/Tools/` with one required string parameter, named consistently with the retrieve-by-id and list tools
- [x] 4.2 Write the tool description so an agent can distinguish it from the list and retrieval tools without calling it, stating that it selects by tag, returns no bodies, and falls back to approximate matching
- [x] 4.3 Project each result to exactly `id`, `title`, `description`, and `category` — no `tags`, no `status`, no `path`, no body, no GitHub URL
- [x] 4.4 State in the response whether results carry the tag exactly or are approximate matches found because no document carries it, naming the tag in both cases, without adding a per-entry field
- [x] 4.5 Return a tool error naming the requirement when the tag is blank, and an empty list as a success naming the tag when nothing matches
- [x] 4.6 Return a tool error identifying the catalog as unavailable when no catalog has ever loaded, never an empty list
- [x] 4.7 Report every failure as a tool result flagged `isError` rather than a protocol error, consistent with the sibling tools
- [x] 4.8 Register the tool in `Program.cs` at the existing composition seam, changing no other host file
- [x] 4.9 Verify the tool performs no trimming, lowercasing, comparison, substring test, or sorting of its own, and depends only on the document service interface

## 5. End-to-end verification and documentation

- [x] 5.1 Run the server against the real repository and confirm an MCP client sees the tag tool listed with its single required parameter alongside the other document tools
- [x] 5.2 Confirm an exact tag returns the expected documents reported as exact, and a partial tag returns approximate matches reported as approximate
- [x] 5.3 Confirm a blank tag, an unmatched tag, and an unloaded catalog produce the three distinct outcomes specified
- [x] 5.4 Add the tool to `README.md`, including how tag values are discovered from the list tool and that the payload omits `tags`
- [x] 5.5 Run `dotnet build` and `dotnet test` from the repository root with no network access and confirm a clean, warning-free build and a passing offline suite
