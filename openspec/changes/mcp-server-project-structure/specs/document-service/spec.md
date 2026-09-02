## ADDED Requirements

### Requirement: The Docs project owns document access

The `HexMaster.CodingStandards.Docs` project SHALL be the only component that reads document content. It SHALL expose a document service interface offering document retrieval by id, an index of all available documents, and keyword search. The MCP host project SHALL reach documents only through that interface, and the Docs project SHALL NOT depend on the host project or on ASP.NET Core hosting types.

#### Scenario: The host consumes the service through its interface

- **WHEN** the host project's document access is inspected
- **THEN** it depends on the document service interface and contains no GitHub, HTTP, or file-reading code of its own

#### Scenario: The service is exercised without a web host

- **WHEN** the unit test project resolves the document service over a fixture document set
- **THEN** retrieval, indexing, and search are all testable with no web host and no network access

### Requirement: Documents are downloaded from GitHub

The document service SHALL obtain the catalog and every document body from the public GitHub repository at runtime. A refresh SHALL download the repository archive for the configured ref in a single request and take the catalog and all document bodies from that one archive, so they are always drawn from the same commit. The repository owner, repository name, and ref SHALL be configurable, defaulting to `hexmasternl/hexmaster-coding-standards` at `main`. An optional access token SHALL be sent when configured and omitted when not.

#### Scenario: Content loads on startup

- **WHEN** the service starts with default configuration and GitHub is reachable
- **THEN** it downloads the archive once and exposes every document listed in that commit's `index.json`

#### Scenario: Catalog and bodies come from one commit

- **WHEN** a refresh completes
- **THEN** every document body served came from the same archive as the catalog that lists it

#### Scenario: A different ref is configured

- **WHEN** the ref is configured to a branch or tag other than `main`
- **THEN** the service serves the content of that ref

#### Scenario: A token is configured

- **WHEN** an access token is present in configuration
- **THEN** the archive request is authenticated with it; otherwise the request is anonymous

### Requirement: Archive extraction is confined to the content prefix

Extraction SHALL only take entries under the archive's `docs/` prefix, and SHALL reject any entry whose resolved destination escapes that prefix — including absolute paths, parent-directory traversal segments, and symbolic or hard links. A rejected entry SHALL be skipped and logged without failing the whole refresh, and its content SHALL NOT be read or written.

#### Scenario: A traversing entry is rejected

- **WHEN** the archive contains an entry resolving outside the `docs/` prefix, such as `docs/../../etc/passwd`
- **THEN** that entry is skipped and logged, and nothing outside the prefix is read or written

#### Scenario: An absolute-path entry is rejected

- **WHEN** the archive contains an entry with an absolute path
- **THEN** that entry is skipped and logged

#### Scenario: Entries outside the content root are ignored

- **WHEN** the archive contains source code, workflows, and other repository files alongside `docs/`
- **THEN** only entries under `docs/` are extracted

### Requirement: Content is cached and refreshed on an interval

The loaded document set SHALL be held in memory and refreshed on a configurable interval, defaulting to 15 minutes. A refresh SHALL replace the cached set atomically, so no request ever observes a partially loaded set. Requests SHALL be served from the cache without per-request network calls.

#### Scenario: Requests do not hit the network

- **WHEN** documents are requested repeatedly between refreshes
- **THEN** every request is served from the cache and no GitHub request is made

#### Scenario: Refreshed content becomes visible

- **WHEN** the catalog at the configured ref changes and a refresh interval elapses
- **THEN** subsequent requests reflect the new content

#### Scenario: A refresh is atomic

- **WHEN** a refresh is in progress
- **THEN** concurrent requests are served entirely from either the previous set or the new one, never a mixture

### Requirement: A failed refresh degrades freshness, not availability

If a refresh fails after content has previously loaded, the service SHALL log the failure, keep serving the last successfully loaded set, and retry on the next interval. If a refresh fails when no content has ever loaded, the service SHALL report itself as not ready and SHALL surface a failure to callers rather than an empty result set.

#### Scenario: GitHub is unreachable after a successful load

- **WHEN** a refresh fails and a previous set is cached
- **THEN** the failure is logged, the cached content continues to be served, and the next interval retries

#### Scenario: GitHub is unreachable on a cold start

- **WHEN** the first load fails and no content is cached
- **THEN** the service reports not ready, and requests fail explicitly rather than reporting zero documents

#### Scenario: An empty result is distinguishable from a failure

- **WHEN** a caller searches for a keyword no document matches, against successfully loaded content
- **THEN** the result is an empty match set, not a failure

### Requirement: The service provides an index of all documents

The document service SHALL return an index of every catalogued document, each carrying its `id`, `title`, `description`, `category`, `status`, and `tags`, without document bodies. The index SHALL reflect the currently cached content set.

#### Scenario: The full index is listed

- **WHEN** the index is requested against loaded content
- **THEN** it contains exactly one entry per catalogued document, each with all six metadata fields and no body

#### Scenario: The index tracks refreshes

- **WHEN** a document is added to the catalog at the configured ref and a refresh completes
- **THEN** the index includes the new document

### Requirement: The service retrieves a document by id

The document service SHALL return a document's metadata together with its full markdown body when given its catalog `id`. Lookup SHALL be exact and case-sensitive on the id. A request for an unknown id SHALL report not-found distinctly from an error, and SHALL NOT fall back to a partial or fuzzy match.

#### Scenario: A known document is retrieved

- **WHEN** a document is requested by an id present in the catalog
- **THEN** its metadata and full markdown body are returned

#### Scenario: An unknown id is requested

- **WHEN** a document is requested by an id absent from the catalog
- **THEN** the result reports not-found, and no other document is returned

#### Scenario: A catalogued document is missing from the archive

- **WHEN** a catalog entry's `path` names a file the archive does not contain
- **THEN** the discrepancy is logged at load time and retrieving that id reports not-found

### Requirement: The service searches documents by keyword

The document service SHALL search the cached content for a caller-supplied keyword, matching case-insensitively against each document's `title`, `description`, `tags`, and markdown body. Results SHALL be returned as index entries, without bodies, ordered so that documents matching in the title or tags rank above those matching only in the body. A blank or whitespace-only keyword SHALL be rejected rather than returning every document.

#### Scenario: A keyword matches metadata and body

- **WHEN** a keyword appears in one document's title and in another's body
- **THEN** both are returned, with the title match ranked first

#### Scenario: Search ignores case

- **WHEN** a keyword is supplied in a different case from the document text it matches
- **THEN** the document is still returned

#### Scenario: A keyword matches a tag

- **WHEN** a keyword equals one of a document's tags
- **THEN** that document is returned and ranked above body-only matches

#### Scenario: No document matches

- **WHEN** a keyword matches no document
- **THEN** an empty result is returned and no failure is reported

#### Scenario: A blank keyword is rejected

- **WHEN** the keyword is empty or only whitespace
- **THEN** the request is rejected and the full document set is not returned

### Requirement: Malformed catalog content is reported, not silently tolerated

If the downloaded `index.json` cannot be parsed, the refresh SHALL fail and be treated as a failed refresh. If the catalog parses but individual entries are invalid — a missing required property, an unknown `category` or `status`, or a duplicate `id` — the invalid entries SHALL be skipped and logged individually, and the remaining valid entries SHALL be served.

#### Scenario: The catalog is not valid JSON

- **WHEN** the downloaded `index.json` cannot be parsed
- **THEN** the refresh fails, the failure is logged, and the previously cached set (if any) continues to be served

#### Scenario: One entry is invalid

- **WHEN** the catalog parses but one entry declares an unknown `category`
- **THEN** that entry is skipped and logged, and the other documents remain available

#### Scenario: Duplicate ids in the catalog

- **WHEN** two entries declare the same `id`
- **THEN** the duplicate is skipped and logged rather than shadowing the first entry silently
