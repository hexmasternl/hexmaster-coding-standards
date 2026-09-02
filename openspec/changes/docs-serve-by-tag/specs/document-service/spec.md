## ADDED Requirements

### Requirement: The service selects documents by tag

The document service SHALL select catalogued documents by a caller-supplied tag, returning index entries without document bodies. Selection SHALL operate on the currently cached catalog, SHALL read catalog metadata only, and SHALL make no network request and fetch no document body. A caller SHALL be able to tell from the result whether the returned documents carry the requested tag exactly or were selected by the fallback rule.

#### Scenario: Documents carrying the tag are selected

- **WHEN** a tag is supplied that at least one catalogued document carries
- **THEN** every document carrying that tag is returned as an index entry, with no document body

#### Scenario: Selection touches no network

- **WHEN** a tag selection is performed against a loaded catalog
- **THEN** no GitHub request is made and no document body is fetched

#### Scenario: The result reports how it matched

- **WHEN** a selection returns results
- **THEN** the result indicates whether the match was exact or produced by the fallback

#### Scenario: Selection follows the cached catalog

- **WHEN** a catalog refresh adds a document carrying the requested tag and a selection is performed afterwards
- **THEN** the new document is included

### Requirement: A supplied tag is normalised before matching

The service SHALL normalise the supplied tag by trimming leading and trailing whitespace and lowercasing it, and SHALL compare it against each catalog tag's own lowercased value using ordinal comparison. A tag consisting only of whitespace, or empty after trimming, SHALL be rejected rather than matching every document or returning the whole catalog.

#### Scenario: Surrounding whitespace is ignored

- **WHEN** the supplied tag has leading or trailing whitespace around a value a document carries
- **THEN** that document is returned

#### Scenario: Matching ignores case

- **WHEN** the supplied tag differs only in case from a tag a document carries
- **THEN** that document is returned

#### Scenario: A blank tag is rejected

- **WHEN** the supplied tag is empty or consists only of whitespace
- **THEN** the request is rejected, and neither an empty result nor the full catalog is returned

### Requirement: Exact matching runs first, and the fallback only when it finds nothing

Selection SHALL run an exact pass that returns every document having a tag equal to the normalised input. Only when that pass returns no document SHALL a fallback pass run, returning every document having a tag that contains the normalised input as a substring. When the exact pass returns at least one document, the fallback SHALL NOT run and SHALL NOT contribute to the result. The two passes' results SHALL NOT be merged.

#### Scenario: An exact match excludes near misses

- **WHEN** the catalog holds a document tagged `ci` and another tagged `cicd`, and the supplied tag is `ci`
- **THEN** only the document tagged `ci` is returned

#### Scenario: The fallback answers a partial tag

- **WHEN** the catalog holds a document tagged `unit-testing`, no document is tagged `testing`, and the supplied tag is `testing`
- **THEN** the document tagged `unit-testing` is returned and the result reports a fallback match

#### Scenario: Neither pass matches

- **WHEN** the supplied tag matches no tag exactly and is contained in no tag
- **THEN** an empty result is returned and no failure is reported

#### Scenario: A document matching on several tags appears once

- **WHEN** a document carries two tags that both contain the normalised input during a fallback pass
- **THEN** that document is returned exactly once

### Requirement: The fallback requires at least two characters

The fallback pass SHALL run only when the normalised tag is at least two characters long. A single-character tag SHALL be matched by the exact pass alone, and SHALL return an empty result rather than every document whose tags contain that character.

#### Scenario: A single character does not trigger the fallback

- **WHEN** the normalised tag is one character long and no document carries it as a whole tag
- **THEN** an empty result is returned, and documents whose tags merely contain that character are not returned

#### Scenario: A single character still matches exactly

- **WHEN** the normalised tag is one character long and a document carries exactly that tag
- **THEN** that document is returned as an exact match

### Requirement: Tag selection results are ordered and complete

Selection SHALL return exactly one entry per matching document, ordered by `category` and then by `id`. The order SHALL NOT depend on catalog order or on the order in which matches were found, and two selections with the same tag over the same cached catalog SHALL return the same entries in the same order.

#### Scenario: Results are ordered deterministically

- **WHEN** a tag matches documents in more than one category
- **THEN** the entries are grouped by category and ordered by id within each category

#### Scenario: Order is independent of match order

- **WHEN** the same tag is selected against the same cached catalog twice
- **THEN** both selections return the same entries in the same order

### Requirement: Invalid catalog entries are not selectable by tag

Entries the service rejected as invalid SHALL NOT be scanned and SHALL NOT appear in a tag selection, and their presence in the catalog SHALL NOT fail the selection.

#### Scenario: An invalid entry cannot match

- **WHEN** the catalog contains an entry rejected as invalid whose tags include the supplied tag
- **THEN** that entry is not returned, and the valid matching entries are

### Requirement: Tag selection over an unloaded catalog fails explicitly

If no catalog has ever loaded, tag selection SHALL surface a failure to the caller rather than an empty result, so a caller can distinguish "nothing is tagged that way" from "the catalog is unavailable". If a catalog is cached but its most recent refresh failed, selection SHALL succeed over the cached catalog.

#### Scenario: The catalog has never loaded

- **WHEN** a tag selection is requested and no catalog has ever loaded
- **THEN** the caller receives a failure, not an empty result

#### Scenario: A stale catalog is selectable

- **WHEN** the cached catalog's most recent refresh failed and a tag selection is requested
- **THEN** the selection succeeds over the cached catalog
