## ADDED Requirements

### Requirement: A tool lists all available documents

The MCP server SHALL expose a tool that returns every catalogued document. The tool SHALL accept no input parameters, and its declared input schema SHALL be empty so a client can call it without supplying arguments. Its name SHALL follow the same naming convention as the retrieve-by-id tool, and its description SHALL state that it returns all documents with metadata and no document bodies, so an agent can tell it apart from the search tool without calling it.

#### Scenario: A client lists the tools

- **WHEN** an MCP client requests the server's tool list
- **THEN** the document list tool is present alongside the retrieve-by-id tool
- **AND** its input schema declares no required or optional parameters

#### Scenario: The tool is called with no arguments

- **WHEN** a client invokes the tool with an empty argument object
- **THEN** the call succeeds and returns the catalogued documents

#### Scenario: Unexpected arguments are tolerated

- **WHEN** a client invokes the tool with an unrecognised argument
- **THEN** the argument is ignored and the full listing is returned

### Requirement: The listing payload carries five metadata fields

Each entry in the response SHALL carry exactly the document's `id`, `title`, `category`, `description`, and `tags`. No entry SHALL include a document body, a file path, or a repository reference. `tags` SHALL be returned as an array, empty when the document has no tags.

#### Scenario: An entry is projected

- **WHEN** a catalogued document is returned in the listing
- **THEN** its entry contains `id`, `title`, `category`, `description`, and `tags`, and nothing else

#### Scenario: Bodies and paths are excluded

- **WHEN** the response is inspected
- **THEN** no entry contains markdown content, a `path`, or any GitHub URL

#### Scenario: A document with no tags

- **WHEN** a catalogued document has an empty tag list
- **THEN** its entry returns `tags` as an empty array rather than omitting the field or returning null

#### Scenario: The listing needs no document reads

- **WHEN** the tool is invoked
- **THEN** the response is produced from catalog metadata alone, and no document body is fetched or read

### Requirement: The listing is complete and stably ordered

The response SHALL contain exactly one entry per catalogued document, with no omissions and no duplicates. Entries SHALL be ordered by `category`, then by `id`, and two calls over the same cached catalog SHALL return byte-identical payloads.

#### Scenario: Every document appears once

- **WHEN** the catalog holds documents across all three categories
- **THEN** the response contains one entry per document and no duplicates

#### Scenario: Ordering is deterministic

- **WHEN** the tool is invoked twice against the same cached catalog
- **THEN** both responses list the same entries in the same order

#### Scenario: Ordering groups categories

- **WHEN** the response is inspected
- **THEN** entries are grouped by category and ordered by id within each category

### Requirement: An empty catalog is a success, an unloaded catalog is a failure

If the catalog has loaded and contains no documents, the tool SHALL return an empty list as a successful result. If no catalog has ever loaded, the tool SHALL report a tool error identifying the catalog as unavailable, and SHALL NOT return an empty list. If a catalog is cached but its refresh most recently failed, the tool SHALL succeed and return the cached listing.

#### Scenario: The catalog is empty

- **WHEN** the catalog has loaded and lists no documents
- **THEN** the tool returns an empty list as a successful result

#### Scenario: The catalog has never loaded

- **WHEN** the first catalog load has failed and nothing is cached
- **THEN** the tool reports an error stating the catalog is unavailable, and does not return an empty list

#### Scenario: Serving a stale catalog

- **WHEN** the cached catalog is past its cache window and the refresh attempt has failed
- **THEN** the tool succeeds and returns the cached listing

### Requirement: Invalid catalog entries are omitted rather than failing the listing

Entries the document service rejected as invalid SHALL NOT appear in the listing, and their presence in the catalog SHALL NOT fail the call. The listing SHALL return the valid entries.

#### Scenario: One entry is invalid

- **WHEN** the catalog contains an entry with an unknown `category` alongside valid entries
- **THEN** the tool returns the valid entries and omits the invalid one, without reporting an error
