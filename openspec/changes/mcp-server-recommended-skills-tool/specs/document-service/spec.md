## ADDED Requirements

### Requirement: The service provides the set of skill candidates

The document service SHALL expose the set of catalogued documents eligible to be turned into agent skills, returned as index entries carrying `id`, `title`, `description`, `category`, `status`, and `tags`, without document bodies. The set SHALL be drawn from the currently cached catalog, SHALL read catalog metadata only, and SHALL make no network request and fetch no document body.

#### Scenario: Eligible documents are returned

- **WHEN** the candidate set is requested against a loaded catalog
- **THEN** every eligible document is returned as an index entry, with no document body

#### Scenario: The candidate set touches no network

- **WHEN** the candidate set is requested
- **THEN** no GitHub request is made and no document body is fetched

#### Scenario: Status is carried

- **WHEN** a candidate is returned
- **THEN** its entry carries the document's `status` alongside its other metadata

#### Scenario: The set follows the cached catalog

- **WHEN** a catalog refresh adds an eligible document and the candidate set is requested afterwards
- **THEN** the new document is included

### Requirement: Superseded and deprecated documents are not skill candidates

The candidate set SHALL exclude every document whose `status` is `superseded` or `deprecated`. Documents whose `status` is `accepted` or `draft` SHALL be included. No other property SHALL affect eligibility: `category`, tags, and subject matter SHALL NOT exclude a document.

#### Scenario: A superseded document is excluded

- **WHEN** the catalog contains a document with `status` `superseded`
- **THEN** it does not appear in the candidate set

#### Scenario: A deprecated document is excluded

- **WHEN** the catalog contains a document with `status` `deprecated`
- **THEN** it does not appear in the candidate set

#### Scenario: A draft document is included

- **WHEN** the catalog contains a document with `status` `draft`
- **THEN** it appears in the candidate set, reporting `draft` as its status

#### Scenario: Category does not affect eligibility

- **WHEN** the catalog contains accepted documents in all three categories
- **THEN** all of them appear in the candidate set

#### Scenario: Subject matter does not affect eligibility

- **WHEN** the catalog contains accepted documents on unrelated subjects
- **THEN** none is excluded on the basis of its subject, tags, or title

### Requirement: The candidate set is ordered and complete

The candidate set SHALL contain exactly one entry per eligible document, ordered by `category` and then by `id`. The order SHALL NOT depend on catalog order, and two requests over the same cached catalog SHALL return the same entries in the same order.

#### Scenario: Each eligible document appears once

- **WHEN** the candidate set is requested
- **THEN** it contains one entry per eligible document and no duplicates

#### Scenario: Ordering is deterministic

- **WHEN** the candidate set is requested twice against the same cached catalog
- **THEN** both requests return the same entries in the same order, grouped by category and ordered by id within each category

### Requirement: Invalid catalog entries are not skill candidates

Entries the service rejected as invalid SHALL NOT appear in the candidate set, and their presence in the catalog SHALL NOT fail the request.

#### Scenario: An invalid entry is not a candidate

- **WHEN** the catalog contains an entry rejected as invalid alongside valid entries
- **THEN** the candidate set contains the valid eligible entries and omits the invalid one, without reporting a failure

### Requirement: An empty candidate set is distinguishable from an unavailable catalog

If the catalog has loaded and no document is eligible, the service SHALL return an empty candidate set as a success. If no catalog has ever loaded, the service SHALL surface a failure rather than an empty set, so a caller can distinguish "nothing is eligible" from "the catalog is unavailable". If a catalog is cached but its most recent refresh failed, the request SHALL succeed over the cached catalog.

#### Scenario: Everything is excluded

- **WHEN** the catalog has loaded and every document is superseded or deprecated
- **THEN** an empty candidate set is returned as a success

#### Scenario: The catalog has never loaded

- **WHEN** the candidate set is requested and no catalog has ever loaded
- **THEN** the caller receives a failure, not an empty set

#### Scenario: A stale catalog still yields candidates

- **WHEN** the cached catalog's most recent refresh failed and the candidate set is requested
- **THEN** the request succeeds over the cached catalog
