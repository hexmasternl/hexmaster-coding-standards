## MODIFIED Requirements

### Requirement: Documents are downloaded from GitHub

The document service SHALL obtain the catalog and every document body from the public GitHub repository at runtime, using the GitHub REST API. A catalog load SHALL fetch `docs/index.json` for the configured ref as a single file in one request, and SHALL NOT download a repository archive. A document body SHALL be fetched individually, by the catalog entry's `path`, only when that document is requested and no unexpired cached copy is held. The repository owner, repository name, and ref SHALL be configurable, defaulting to `hexmasternl/hexmaster-coding-standards` at `main`, and SHALL apply to both the catalog request and every body request. An optional access token SHALL be sent when configured and omitted when not.

Because the catalog is fetched independently of document bodies, the service SHALL NOT assume that a catalog and the bodies it references come from the same commit, and SHALL report a catalogued document whose body cannot be fetched as an upstream failure distinct from an unknown id.

#### Scenario: The catalog loads at startup

- **WHEN** the service starts with default configuration and GitHub is reachable
- **THEN** it fetches `docs/index.json` for the configured ref once and exposes every valid entry it lists
- **AND** it fetches no document bodies

#### Scenario: No archive is downloaded

- **WHEN** the service loads its catalog
- **THEN** it requests only the catalog file, and no repository archive is fetched or extracted

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

#### Scenario: The catalog and a body disagree

- **WHEN** the cached catalog lists a document whose body GitHub cannot resolve at the configured ref
- **THEN** the failure is reported as an upstream body failure, not as an unknown id, and the entry remains in the catalog

### Requirement: Content is cached and refreshed on an interval

The catalog SHALL be loaded once at startup, so that a replica can answer a readiness probe before it has served any request. Thereafter the cached catalog SHALL expire after a configurable time-to-live, defaulting to 30 minutes, evaluated when the catalog is requested; the first request after expiry SHALL trigger the fetch and be served from its result. The service SHALL NOT refresh the catalog on a recurring background timer. A completed load SHALL replace the cached catalog atomically, so no request observes a partially loaded catalog. Requests arriving inside the window SHALL be served from the cache with no GitHub request. When several requests arrive concurrently on an expired cache, exactly one fetch SHALL be issued and the other callers SHALL await its outcome.

Document bodies SHALL be cached in memory individually, keyed by the content path they were fetched from, and each cached body SHALL expire a configurable duration after the moment it was fetched, defaulting to 30 minutes. Expiry SHALL be absolute from the fetch instant and SHALL NOT be extended by subsequent reads. A request served from an unexpired cached body SHALL make no network call.

#### Scenario: The catalog is loaded before the first request

- **WHEN** the service has started and no request has yet arrived
- **THEN** the catalog has been loaded, and a readiness check reports the service able to serve

#### Scenario: Requests inside the window do not hit the network

- **WHEN** the catalog is requested repeatedly within the cache window
- **THEN** every request is served from the cache and no GitHub request is made

#### Scenario: The first request after expiry refreshes

- **WHEN** the cache window has elapsed and the catalog is requested
- **THEN** that request triggers a catalog fetch and is served from its result

#### Scenario: No recurring background refresh occurs

- **WHEN** the service has completed its startup load and is then idle for longer than the cache window
- **THEN** no further catalog fetch is issued until a request arrives

#### Scenario: Concurrent callers share one fetch

- **WHEN** several requests arrive simultaneously on an expired cache
- **THEN** exactly one catalog fetch is issued and every caller is served from that one result

#### Scenario: A load is atomic

- **WHEN** a catalog load is in progress
- **THEN** concurrent requests are served entirely from either the previous catalog or the new one, never a mixture

#### Scenario: The catalog window is configurable

- **WHEN** the catalog cache time-to-live is configured to a value other than the default
- **THEN** the service expires the cached catalog after that duration

#### Scenario: A repeated body request is served from cache

- **WHEN** the same document is requested twice within the body cache lifetime
- **THEN** the second request is served from memory and no GitHub request is made

#### Scenario: An expired body is refetched

- **WHEN** a document is requested after its cached body's lifetime has elapsed
- **THEN** the body is fetched from GitHub again and the refreshed copy is cached

#### Scenario: Reading does not extend the body lifetime

- **WHEN** a document is requested repeatedly throughout its cache lifetime and then requested again after the lifetime has elapsed from the original fetch
- **THEN** that last request refetches the body rather than serving the original copy

#### Scenario: The body lifetime is configurable

- **WHEN** the body cache lifetime is supplied as configuration
- **THEN** cached bodies expire after the configured duration rather than the default 30 minutes

#### Scenario: A repointed entry misses the cache

- **WHEN** a catalog load changes an entry's `path` to a different file
- **THEN** the next request for that id fetches the new file rather than serving the cached body of the old one

#### Scenario: Expired bodies do not accumulate

- **WHEN** a catalog load completes
- **THEN** cached bodies whose lifetime has elapsed are discarded

### Requirement: A failed refresh degrades freshness, not availability

If a catalog fetch fails after a catalog has previously loaded, the service SHALL log the failure, keep serving the last successfully loaded catalog however stale, and retry on the next request that finds the cache expired. If a catalog fetch fails when no catalog has ever loaded, the service SHALL report itself as not ready and SHALL surface a failure to callers rather than an empty result set. A failed body fetch SHALL NOT be treated as a failed catalog load and SHALL NOT affect readiness.

#### Scenario: GitHub is unreachable after a successful load

- **WHEN** a catalog fetch fails and a previous catalog is cached
- **THEN** the failure is logged, the cached catalog continues to be served, and the next request after expiry retries

#### Scenario: GitHub is unreachable on a cold start

- **WHEN** the first catalog load fails and nothing is cached
- **THEN** the service reports not ready, and requests fail explicitly rather than reporting zero documents

#### Scenario: A body fetch failure does not affect readiness

- **WHEN** the catalog has loaded and a body fetch fails
- **THEN** the service remains ready and continues to serve other documents

#### Scenario: An empty result is distinguishable from a failure

- **WHEN** a caller lists or searches against a successfully loaded catalog that matches nothing
- **THEN** the result is an empty set, not a failure

#### Scenario: Repeated failures do not clear the cache

- **WHEN** several consecutive catalog fetches fail
- **THEN** the last successfully loaded catalog is still served and is never replaced with an empty one

## ADDED Requirements

### Requirement: The service provides a listing projection

The document service SHALL expose each catalogued document as a listing entry carrying exactly its `id`, `title`, `category`, `description`, and `tags`, and no other property. `tags` SHALL be an array, empty rather than absent when the document has no tags. The projection SHALL be built from the cached catalog alone, with no document body read, SHALL be ordered by `category` then `id`, and SHALL contain exactly one entry per valid catalogued document. The service's own index entry SHALL continue to carry `status`, so a caller that needs it is not deprived of it by this projection.

#### Scenario: An entry carries five fields

- **WHEN** a catalogued document is projected into a listing entry
- **THEN** the entry exposes `id`, `title`, `category`, `description`, and `tags`, and nothing else

#### Scenario: The projection reads no bodies

- **WHEN** the listing is produced
- **THEN** no document body is fetched or read

#### Scenario: Documents with no tags

- **WHEN** a catalogued document has no tags
- **THEN** its entry carries an empty `tags` array rather than a null or absent value

#### Scenario: The listing is ordered and complete

- **WHEN** the listing is produced from a catalog spanning all three categories
- **THEN** it contains one entry per valid document, grouped by category and ordered by id within each category

#### Scenario: Invalid entries are absent

- **WHEN** the catalog contained an entry rejected as invalid
- **THEN** that entry does not appear in the listing, and the listing is produced without failing

#### Scenario: Status survives on the index

- **WHEN** the service's index is requested
- **THEN** each of its entries still carries `status`
