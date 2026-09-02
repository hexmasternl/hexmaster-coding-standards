# Tasks — Serve a document by id

> Prerequisite: `mcp-server-project-structure` must be implemented and archived first. This change edits the `Docs` project, the `Tools` folder, and `Program.cs` that change creates.

## 1. Configuration and contracts

- [x] 1.1 Add `BodyCacheLifetime` to the Docs options type, defaulting to 30 minutes, bound from configuration and overridable by environment variable alongside the existing owner, repository, ref, and token settings
- [x] 1.2 Validate the options on startup: reject a non-positive `BodyCacheLifetime` with a message naming the setting, and keep the token out of any validation or startup log output
- [x] 1.3 Define the retrieval result type so a caller can distinguish `Found`, `NotFound` (no catalog entry), and `Unavailable` (catalogued, body not obtainable) without parsing a message string
- [x] 1.4 Update the document service interface's retrieve-by-id member to return that result type, and record the body's fetch instant on the found case

## 2. Catalog path validation

- [x] 2.1 Add a path validator that accepts only relative POSIX paths rooted at `docs/` inside `ADR`, `Designs`, or `Structures`, rejecting `..` segments, backslashes, absolute paths, and anything carrying a scheme or host
- [x] 2.2 URL-encode each path segment when composing the GitHub API path, so spaces and other reserved characters round-trip
- [x] 2.3 Log a rejected path at warning with the entry's `id` and the offending value, without failing the catalog load or affecting other entries
- [x] 2.4 Unit-test the validator: traversal, absolute, scheme-and-host, outside-`docs/`, outside a category folder, valid path needing encoding, and that a single bad entry leaves the rest of the catalog retrievable

## 3. GitHub client: fetch one body

- [x] 3.1 Add a client method that fetches a single file's raw bytes via `GET /repos/{owner}/{repo}/contents/{path}?ref={ref}` with `Accept: application/vnd.github.raw` and the configured user agent
- [x] 3.2 Send the optional access token when configured and omit the header entirely when not
- [x] 3.3 Map responses: success to body text, 404 to a missing-file outcome, 403/429 with rate-limit headers to a distinct rate-limited outcome, other non-success and transport failures to a general fetch failure
- [x] 3.4 Ensure no failure path puts the token, request headers, or a stack trace into a message returned to a caller
- [x] 3.5 Unit-test each mapping against a fake `HttpMessageHandler`, asserting the composed URL, the ref query, and the presence or absence of the authorization header — with no network access

## 4. Per-document body cache

- [x] 4.1 Implement the cache over `ConcurrentDictionary`, keyed by resolved content path, holding the body and its fetch instant, with time read from an injected `TimeProvider`
- [x] 4.2 Make expiry absolute from the fetch instant so repeated reads never extend a cached body's lifetime
- [x] 4.3 Add single-flight per key, so concurrent requests for one uncached document produce exactly one fetch and all await the same outcome
- [x] 4.4 Remove the entry on a failed fetch so the next request retries, and ensure every waiting request in a shared failure observes the failure
- [x] 4.5 Sweep expired entries when a catalog refresh completes, so an idle replica does not hold content indefinitely
- [x] 4.6 Register the cache and `TimeProvider.System` in the Docs project's single DI registration method, with no change required in the host
- [x] 4.7 Unit-test with a fake time provider: hit inside the window makes no fetch, expiry past the window refetches, repeated reads do not extend the lifetime, a configured non-default lifetime is honoured, a concurrent burst yields one fetch, a failed shared fetch is not retained, and a refresh discards expired entries

## 5. Retrieval by id

- [x] 5.1 Resolve the id against the cached catalog with an exact, case-sensitive match, returning `NotFound` with no network call and no fuzzy or partial fallback
- [x] 5.2 On a catalog hit, validate the entry's `path`, then serve the body from the cache or fetch and cache it, returning metadata plus the full markdown body
- [x] 5.3 Return `Unavailable` when the path is rejected or the fetch fails, logging the id, the path, and the underlying cause
- [x] 5.4 Keep body-fetch failures out of the readiness signal, so `GET /health` still reports healthy whenever the catalog has loaded
- [x] 5.5 Unit-test: known id returns metadata and full body, unknown id returns `NotFound` with zero HTTP calls, a repointed `path` after a refresh fetches the new file, a catalogued file missing at the ref returns `Unavailable`, and a recovered fetch succeeds on retry

## 6. Remove the archive path

- [x] 6.1 Delete the archive download, extraction, and `docs/`-prefix confinement code, and switch catalog loading to a single `docs/index.json` fetch through the GitHub client
- [x] 6.2 Delete the archive extraction tests, having re-expressed their traversal and absolute-path cases as catalog-path validation tests in 2.4
- [x] 6.3 Confirm the catalog refresh interval, atomic swap, cold-start readiness, and malformed-catalog handling still behave as specified after the switch, and adjust their tests to the new fetch mechanism

## 7. The MCP tool

- [x] 7.1 Add `GetDocumentTool` to `src/HexMaster.CodingStandards.Mcp/Tools/`, taking one required string id, described so a client can tell the value comes from the document catalog
- [x] 7.2 Return metadata and the complete markdown body on success, untruncated and unsummarised
- [x] 7.3 Reject an empty or whitespace-only id as a tool error before any lookup or network call
- [x] 7.4 Map `NotFound` to an error result naming the id and stating no such document is catalogued, and `Unavailable` to a distinct error result stating the document exists but its content could not be retrieved
- [x] 7.5 Return both as tool results flagged `isError` rather than protocol errors, so the model sees the failure in the tool result
- [x] 7.6 Register the tool in `Program.cs` at the existing composition seam, changing no other host file
- [x] 7.7 Verify the tool class holds no HTTP client, no GitHub URL construction, and no caching of its own

## 8. End-to-end verification and documentation

- [ ] 8.1 Run the server against the real repository and confirm an MCP client lists the tool, retrieves a known document whole, and gets distinguishable errors for an unknown id and for a catalogued document whose file is absent at the ref
- [ ] 8.2 Confirm a second retrieval of the same document inside the lifetime issues no GitHub request, and that `GET /health` stays 200 after repeated body-fetch failures
- [ ] 8.3 Rewrite the `CLAUDE.md` architecture notes that describe archive download, extraction confinement, and the 15-minute body cache, replacing them with per-document fetching, the two staleness windows, the rate-limit consideration, and catalog-path validation
- [ ] 8.4 Document the `BodyCacheLifetime` setting and the rate-limit reason to configure an access token in `README.md`
- [ ] 8.5 Run `dotnet build` and `dotnet test` from the repository root with no network access and confirm a clean, warning-free build and a passing offline suite
