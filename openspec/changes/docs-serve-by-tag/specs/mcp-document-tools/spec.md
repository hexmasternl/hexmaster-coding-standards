## ADDED Requirements

### Requirement: A tool finds documents by tag

The MCP server SHALL expose a tool that takes a single required string naming a tag and returns the catalogued documents carrying it. Its name SHALL follow the same naming convention as the retrieve-by-id and list tools, and its description SHALL state that it selects documents by tag, returns metadata without document bodies, and falls back to approximate matching when no document carries the tag exactly — so an agent can tell it apart from the list and retrieval tools without calling it.

#### Scenario: A client lists the tools

- **WHEN** an MCP client requests the server's tool list
- **THEN** the tag tool is present alongside the retrieve-by-id and list tools
- **AND** its input schema declares exactly one required string parameter for the tag

#### Scenario: A tag is supplied

- **WHEN** the tool is invoked with a tag carried by at least one catalogued document
- **THEN** the call succeeds and returns the matching documents

#### Scenario: The selection needs no document reads

- **WHEN** the tool is invoked
- **THEN** the response is produced from catalog metadata alone, and no document body is fetched or read

### Requirement: The tag payload carries four metadata fields

Each entry in the response SHALL carry exactly the document's `id`, `title`, `description`, and `category`. No entry SHALL include the document's tags, its `status`, a document body, a file path, or a repository reference.

#### Scenario: An entry is projected

- **WHEN** a matching document is returned
- **THEN** its entry contains `id`, `title`, `description`, and `category`, and nothing else

#### Scenario: Bodies, paths, and tags are excluded

- **WHEN** the response is inspected
- **THEN** no entry contains markdown content, a `path`, a GitHub URL, a `tags` array, or a `status`

### Requirement: Tag results are complete and stably ordered

The response SHALL contain exactly one entry per matching document, with no omissions and no duplicates, including when a document carries more than one tag satisfying the query. Entries SHALL be ordered by `category`, then by `id`, and two calls with the same tag over the same cached catalog SHALL return identical payloads.

#### Scenario: Every match appears once

- **WHEN** the tag matches documents across more than one category
- **THEN** the response contains one entry per matching document and no duplicates

#### Scenario: A document matching twice appears once

- **WHEN** a document carries two tags that both satisfy the query under the fallback rule
- **THEN** that document appears exactly once in the response

#### Scenario: Ordering is deterministic

- **WHEN** the tool is invoked twice with the same tag against the same cached catalog
- **THEN** both responses list the same entries in the same order, grouped by category and ordered by id within each category

### Requirement: The response distinguishes exact matches from fallback matches

The tool's response SHALL state whether the returned documents carry the requested tag exactly, or were found approximately because no document carries it exactly. Both forms SHALL name the requested tag. This distinction SHALL be carried by the response itself and SHALL NOT be expressed as an additional field on any entry.

#### Scenario: An exact match is reported as exact

- **WHEN** at least one document carries the requested tag exactly
- **THEN** the response states the results are documents carrying that tag

#### Scenario: A fallback match is reported as approximate

- **WHEN** no document carries the requested tag exactly and the fallback returns results
- **THEN** the response states that no document carries the requested tag and that the results are approximate matches
- **AND** names the requested tag

#### Scenario: The distinction is not a per-entry field

- **WHEN** a fallback result is inspected
- **THEN** each entry still carries exactly the four metadata fields, with no match-quality field added

### Requirement: A blank tag is rejected and no match is a success

The tool SHALL reject an empty or whitespace-only tag as a tool error stating that a tag is required, without scanning the catalog. When a tag is supplied and neither the exact pass nor the fallback matches any document, the tool SHALL return an empty list as a successful result rather than an error.

#### Scenario: A blank tag is rejected

- **WHEN** the tool is invoked with an empty or whitespace-only tag
- **THEN** the result is flagged as an error stating a tag is required
- **AND** no catalog scan is performed

#### Scenario: Nothing matches

- **WHEN** a tag is supplied that neither exactly nor approximately matches any document's tags
- **THEN** the tool returns an empty list as a successful result

#### Scenario: An empty result names the tag

- **WHEN** an empty result is returned
- **THEN** the response states that no document is tagged with the requested tag, naming it

### Requirement: An unloaded catalog is a failure, a stale catalog is a success

If no catalog has ever loaded, the tag tool SHALL report a tool error identifying the catalog as unavailable, and SHALL NOT return an empty list. If a catalog is cached but its most recent refresh failed, the tool SHALL succeed and select over the cached catalog.

#### Scenario: The catalog has never loaded

- **WHEN** the first catalog load has failed and nothing is cached
- **THEN** the tool reports an error stating the catalog is unavailable, and does not return an empty list

#### Scenario: Selecting over a stale catalog

- **WHEN** the cached catalog is past its cache window and the refresh attempt has failed
- **THEN** the tool succeeds and returns matches from the cached catalog

### Requirement: The tag tool holds no selection logic

The tool class SHALL live in the host project's `Tools` folder, SHALL be registered at composition time in `Program.cs`, and SHALL reach documents only through the Docs project's document service interface. It SHALL perform no tag normalisation, no matching, and no ordering of its own; it SHALL validate that a tag was supplied, call the service, project the four fields, and format the response.

#### Scenario: The tool is registered at the seam

- **WHEN** `Program.cs` is inspected
- **THEN** the tag tool is registered alongside the other document tools, with no other host file changed to accommodate it

#### Scenario: The tool delegates

- **WHEN** the tool class is inspected
- **THEN** it contains no lowercasing, trimming, comparison, substring test, or sort over tags, and its only document dependency is the document service interface
