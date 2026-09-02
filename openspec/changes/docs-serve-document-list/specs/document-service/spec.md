## MODIFIED Requirements

### Requirement: Documents are downloaded from GitHub

The document service SHALL obtain the catalog from the public GitHub repository at runtime. A catalog load SHALL fetch `docs/index.json` for the configured ref as a single file in one request, and SHALL NOT download a repository archive. The repository owner, repository name, and ref SHALL be configurable, defaulting to `hexmasternl/hexmaster-coding-standards` at `main`. An optional access token SHALL be sent when configured and omitted when not.

Because the catalog is fetched independently of document bodies, the service SHALL NOT assume that a catalog and the bodies it references come from the same commit, and SHALL report a catalogued document whose body cannot be fetched as an upstream failure distinct from an unknown id.

#### Scenario: The catalog loads on first use

- **WHEN** the catalog is requested for the first time and GitHub is reachable
- **THEN** the service fetches `docs/index.json` for the configured ref in a single request and exposes every valid entry it lists

#### Scenario: No archive is downloaded

- **WHEN** the service loads its catalog
- **THEN** it requests only the catalog file, and no repository archive is fetched or extracted

#### Scenario: A different ref is configured

- **WHEN** the ref is configured to a branch or tag other than `main`
- **THEN** the service serves the catalog of that ref

#### Scenario: A token is configured

- **WHEN** an access token is present in configuration
- **THEN** the catalog request is authenticated with it; otherwise the request is anonymous

#### Scenario: The catalog and a body disagree

- **WHEN** the cached catalog lists a document whose body GitHub cannot resolve at the configured ref
- **THEN** the failure is reported as an upstream body failure, not as an unknown id, and the entry remains in the catalog

### Requirement: Content is cached and refreshed on an interval

The loaded catalog SHALL be held in memory and SHALL expire after a configurable time-to-live, defaulting to 30 minutes. Expiry SHALL be evaluated when the catalog is requested, and the first request after expiry SHALL trigger the fetch; the service SHALL NOT refresh on a background timer. A completed load SHALL replace the cached catalog atomically, so no request observes a partially loaded catalog. Requests arriving inside the window SHALL be served from the cache with no GitHub request. When several requests arrive concurrently on an expired cache, exactly one fetch SHALL be issued and the other callers SHALL await its outcome.

#### Scenario: Requests inside the window do not hit the network

- **WHEN** the catalog is requested repeatedly within the cache window
- **THEN** every request is served from the cache and no GitHub request is made

#### Scenario: The first request after expiry refreshes

- **WHEN** the cache window has elapsed and the catalog is requested
- **THEN** that request triggers a catalog fetch and is served from its result

#### Scenario: No background refresh occurs

- **WHEN** the service is idle for longer than the cache window
- **THEN** no catalog fetch is issued until a request arrives

#### Scenario: Concurrent callers share one fetch

- **WHEN** several requests arrive simultaneously on an expired cache
- **THEN** exactly one catalog fetch is issued and every caller is served from that one result

#### Scenario: A load is atomic

- **WHEN** a catalog load is in progress
- **THEN** concurrent requests are served entirely from either the previous catalog or the new one, never a mixture

#### Scenario: The window is configurable

- **WHEN** the cache time-to-live is configured to a value other than the default
- **THEN** the service expires the cached catalog after that duration

### Requirement: A failed refresh degrades freshness, not availability

If a catalog fetch fails after a catalog has previously loaded, the service SHALL log the failure, keep serving the last successfully loaded catalog however stale, and retry on the next request that finds the cache expired. If a catalog fetch fails when no catalog has ever loaded, the service SHALL report itself as not ready and SHALL surface a failure to callers rather than an empty result set.

#### Scenario: GitHub is unreachable after a successful load

- **WHEN** a catalog fetch fails and a previous catalog is cached
- **THEN** the failure is logged, the cached catalog continues to be served, and the next request after expiry retries

#### Scenario: GitHub is unreachable on a cold start

- **WHEN** the first catalog load fails and nothing is cached
- **THEN** the service reports not ready, and requests fail explicitly rather than reporting zero documents

#### Scenario: An empty result is distinguishable from a failure

- **WHEN** a caller lists or searches against a successfully loaded catalog that matches nothing
- **THEN** the result is an empty set, not a failure

#### Scenario: Repeated failures do not clear the cache

- **WHEN** several consecutive catalog fetches fail
- **THEN** the last successfully loaded catalog is still served and is never replaced with an empty one

## REMOVED Requirements

### Requirement: Archive extraction is confined to the content prefix

**Reason**: No repository archive is downloaded any more. The catalog is fetched as a single file and document bodies are fetched individually, so there is no archive to extract and no extraction boundary to enforce. Keeping the requirement would mandate hardening for an input the service no longer accepts, and keeping the code would leave unused tarball-extraction logic in the tree.

**Migration**: The untrusted input that remains is the catalog's `path` values, which are interpolated into GitHub request URLs. Validating those values before use is covered by the body-fetching requirements introduced in the `docs-serve-document-by-id` change. If that change is archived first and has already removed this requirement, drop this removal from the delta rather than removing it twice.
