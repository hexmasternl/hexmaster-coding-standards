## MODIFIED Requirements

### Requirement: Documents are downloaded from GitHub

The document service SHALL obtain the catalog and every document body from the public GitHub repository at runtime, using the GitHub REST API. A catalog load SHALL fetch `docs/index.json` in a single request. A document body SHALL be fetched individually, by the catalog entry's `path`, only when that document is requested and no unexpired cached copy is held. The repository owner, repository name, and ref SHALL be configurable, defaulting to `hexmasternl/hexmaster-coding-standards` at `main`, and SHALL apply to both the catalog request and every body request. An optional access token SHALL be sent when configured and omitted when not.

#### Scenario: The catalog loads on startup

- **WHEN** the service starts with default configuration and GitHub is reachable
- **THEN** it fetches `docs/index.json` once and exposes every document listed in it
- **AND** it fetches no document bodies

#### Scenario: A body is fetched on first request

- **WHEN** a document is requested and no unexpired cached body is held for it
- **THEN** the service fetches that one document's body from the GitHub API at the configured ref
- **AND** fetches no other document's body

#### Scenario: A different ref is configured

- **WHEN** the ref is configured to a branch or tag other than `main`
- **THEN** both the catalog request and every body request target that ref

#### Scenario: A token is configured

- **WHEN** an access token is present in configuration
- **THEN** the catalog and body requests are authenticated with it; otherwise the requests are anonymous

#### Scenario: The token stays out of logs

- **WHEN** a request or a failure is logged
- **THEN** the log output contains no access token value

### Requirement: Content is cached and refreshed on an interval

The catalog SHALL be held in memory and refreshed on a configurable interval, defaulting to 15 minutes, and a refresh SHALL replace the cached catalog atomically so no request observes a partially loaded catalog. Document bodies SHALL be cached in memory individually, keyed by the content path they were fetched from, and each cached body SHALL expire a configurable duration after the moment it was fetched, defaulting to 30 minutes. Expiry SHALL be absolute from the fetch instant and SHALL NOT be extended by subsequent reads. A request served from an unexpired cached body SHALL make no network call.

#### Scenario: A repeated request is served from cache

- **WHEN** the same document is requested twice within the body cache lifetime
- **THEN** the second request is served from memory and no GitHub request is made

#### Scenario: An expired body is refetched

- **WHEN** a document is requested after its cached body's lifetime has elapsed
- **THEN** the body is fetched from GitHub again and the refreshed copy is cached

#### Scenario: Reading does not extend the lifetime

- **WHEN** a document is requested repeatedly throughout its cache lifetime and then requested again after the lifetime has elapsed from the original fetch
- **THEN** that last request refetches the body rather than serving the original copy

#### Scenario: The lifetime is configurable

- **WHEN** the body cache lifetime is supplied as configuration
- **THEN** cached bodies expire after the configured duration rather than the default 30 minutes

#### Scenario: A catalog refresh is atomic

- **WHEN** a catalog refresh is in progress
- **THEN** concurrent requests observe entirely the previous catalog or entirely the new one, never a mixture

#### Scenario: A repointed entry misses the cache

- **WHEN** a catalog refresh changes an entry's `path` to a different file
- **THEN** the next request for that id fetches the new file rather than serving the cached body of the old one

#### Scenario: Expired entries do not accumulate

- **WHEN** a catalog refresh completes
- **THEN** cached bodies whose lifetime has elapsed are discarded

### Requirement: A failed refresh degrades freshness, not availability

If a catalog refresh fails after a catalog has previously loaded, the service SHALL log the failure, keep serving the last successfully loaded catalog, and retry on the next interval. If a catalog load fails when no catalog has ever loaded, the service SHALL report itself as not ready and SHALL surface a failure to callers rather than an empty result set. A failed body fetch SHALL NOT be treated as a failed catalog refresh and SHALL NOT affect readiness.

#### Scenario: GitHub is unreachable after a successful catalog load

- **WHEN** a catalog refresh fails and a previous catalog is cached
- **THEN** the failure is logged, the cached catalog continues to be served, and the next interval retries

#### Scenario: GitHub is unreachable on a cold start

- **WHEN** the first catalog load fails and no catalog is cached
- **THEN** the service reports not ready, and requests fail explicitly rather than reporting zero documents

#### Scenario: A body fetch failure does not affect readiness

- **WHEN** the catalog has loaded and a body fetch fails
- **THEN** the service remains ready and continues to serve other documents

#### Scenario: An empty result is distinguishable from a failure

- **WHEN** a caller searches for a keyword no document matches, against a successfully loaded catalog
- **THEN** the result is an empty match set, not a failure

### Requirement: The service retrieves a document by id

The document service SHALL return a document's metadata together with its full markdown body when given its catalog `id`. Lookup SHALL be exact and case-sensitive on the id, and SHALL resolve against the currently cached catalog. A request for an unknown id SHALL report not-found without any network call, SHALL NOT fall back to a partial or fuzzy match, and SHALL be distinguishable by the caller from a failure to obtain the body of a document that is catalogued.

#### Scenario: A known document is retrieved

- **WHEN** a document is requested by an id present in the catalog
- **THEN** its metadata and full markdown body are returned

#### Scenario: An unknown id is requested

- **WHEN** a document is requested by an id absent from the catalog
- **THEN** the result reports not-found, no other document is returned, and no GitHub request is made

#### Scenario: A catalogued document is missing at the ref

- **WHEN** a catalog entry's `path` names a file GitHub does not have at the configured ref
- **THEN** the result reports the body as unavailable, distinctly from not-found, and the failure is logged with the id and path

#### Scenario: Not-found and unavailable are distinguishable

- **WHEN** a caller receives a failure for an unknown id and a failure for a catalogued document whose body could not be fetched
- **THEN** the two outcomes are distinguishable without parsing a message string

## ADDED Requirements

### Requirement: Catalog paths are validated before a body is requested

Before a catalog entry's `path` is used to build a GitHub API request, the service SHALL verify that it is a relative POSIX path, contains no `..` segment and no backslash, is not absolute or rooted at a scheme or host, and resolves under the `docs/` content root inside one of the three category folders. Each path segment SHALL be URL-encoded when composing the request. A `path` failing validation SHALL be rejected, no request SHALL be made for it, and the rejection SHALL be logged naming the entry's `id` and the offending path, without failing the catalog load or affecting other entries.

#### Scenario: A traversing path is rejected

- **WHEN** a catalog entry's `path` is `docs/../../etc/passwd`
- **THEN** no GitHub request is made for it, the rejection is logged, and requesting that id reports the body as unavailable

#### Scenario: An absolute or remote path is rejected

- **WHEN** a catalog entry's `path` is an absolute path or contains a scheme and host
- **THEN** it is rejected and logged, and no request is made to that location

#### Scenario: A path outside the content root is rejected

- **WHEN** a catalog entry's `path` points outside `docs/` or outside the three category folders
- **THEN** it is rejected and logged, and no request is made for it

#### Scenario: One bad path does not break the catalog

- **WHEN** a single entry's `path` fails validation
- **THEN** the catalog still loads and every other document remains retrievable

#### Scenario: Unusual but valid path characters are encoded

- **WHEN** a valid `path` under a category folder contains spaces or other characters requiring encoding
- **THEN** each segment is URL-encoded and the document is fetched successfully

### Requirement: A failed body fetch is isolated and not cached

A body fetch that fails — because the file is missing at the ref, the request is rate-limited, GitHub returns an error, or the request times out — SHALL be logged with the document's `id`, reported to the caller as an unavailable body, and SHALL NOT be stored in the body cache. The next request for that document SHALL attempt the fetch again. A failure for one document SHALL NOT affect any other document, the cached catalog, or readiness.

#### Scenario: A failure is retried on the next request

- **WHEN** a body fetch fails and the same document is requested again
- **THEN** a fresh fetch is attempted rather than a cached failure being replayed

#### Scenario: A recovered document is served

- **WHEN** a body fetch fails, the underlying cause is resolved, and the document is requested again
- **THEN** the body is fetched successfully, cached, and returned

#### Scenario: A rate-limited request degrades one document

- **WHEN** GitHub rate-limits a body request
- **THEN** that document reports its body as unavailable, the rate-limit response is logged, and documents already cached continue to be served

#### Scenario: Other documents are unaffected

- **WHEN** one document's body fetch fails
- **THEN** requests for other documents succeed as normal

### Requirement: Concurrent requests for one uncached document make one fetch

When several requests for the same uncached document are in flight at once, the service SHALL issue a single GitHub request and satisfy every waiting request from its outcome. If that fetch fails, every waiting request SHALL observe the failure, and no failed result SHALL be retained for later requests.

#### Scenario: A burst produces one request

- **WHEN** several concurrent requests ask for the same document whose body is not cached
- **THEN** exactly one GitHub request is made and all requests receive the same body

#### Scenario: A shared failure is not retained

- **WHEN** the single in-flight fetch shared by concurrent requests fails
- **THEN** every waiting request reports the body as unavailable, and a later request attempts a fresh fetch

### Requirement: The body cache lifetime is exercised without waiting

The body cache SHALL take its notion of time from an injected time abstraction rather than reading the system clock directly, so that expiry behaviour is verifiable in unit tests without real delays and without network access.

#### Scenario: Expiry is tested offline

- **WHEN** a test advances the injected time past the configured body cache lifetime
- **THEN** the next request refetches the body, with no real waiting and no network call

## REMOVED Requirements

### Requirement: Archive extraction is confined to the content prefix

**Reason**: The service no longer downloads or extracts a repository archive. The catalog is fetched as a single file and bodies are fetched individually by path, so there is no archive to extract and no extraction destination for an entry to escape. The untrusted input that remains is the `path` value inside the catalog.

**Migration**: The safety property this requirement protected is carried by the new "Catalog paths are validated before a body is requested" requirement, which rejects traversal segments, absolute paths, and paths outside the `docs/` content root before a `path` is used to compose a GitHub API request. Any archive download, extraction, and prefix-confinement code is deleted along with its tests; the equivalent traversal and absolute-path cases are re-expressed as catalog-path validation tests.
