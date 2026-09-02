## ADDED Requirements

### Requirement: A tool recommends skills built from the coding standards

The MCP server SHALL expose a tool that returns the catalogued documents as candidates for agent skills, together with instructions for turning them into skills. The tool SHALL accept no input parameters, and its declared input schema SHALL be empty. Its name SHALL follow the same naming convention as the other document tools, and its description SHALL state that it returns skill candidates and authoring instructions rather than document content, so an agent can tell it apart from the retrieval, listing, and tag tools without calling it.

#### Scenario: A client lists the tools

- **WHEN** an MCP client requests the server's tool list
- **THEN** the skill recommendation tool is present alongside the retrieval, listing, and tag tools
- **AND** its input schema declares no required or optional parameters

#### Scenario: The tool is called with no arguments

- **WHEN** a client invokes the tool with an empty argument object
- **THEN** the call succeeds and returns the candidates together with the instructions

#### Scenario: The tool needs no document reads

- **WHEN** the tool is invoked
- **THEN** the response is produced from catalog metadata alone, and no document body is fetched or read

### Requirement: The candidate payload carries six metadata fields and no body

Each candidate SHALL carry exactly the document's `id`, `title`, `description`, `category`, `status`, and `tags`. No candidate SHALL include a document body, a file path, or a repository reference. `tags` SHALL be returned as an array, empty when the document has no tags. Candidates SHALL be ordered by `category`, then by `id`, and two calls over the same cached catalog SHALL return identical payloads.

#### Scenario: A candidate is projected

- **WHEN** an eligible document is returned as a candidate
- **THEN** its entry contains `id`, `title`, `description`, `category`, `status`, and `tags`, and nothing else

#### Scenario: Bodies and paths are excluded

- **WHEN** the response is inspected
- **THEN** no candidate contains markdown content, a `path`, or any GitHub URL

#### Scenario: Status is carried through

- **WHEN** a candidate's document is `draft`
- **THEN** its entry reports `draft`, so the caller can weigh it differently from an `accepted` document

#### Scenario: Ordering is deterministic

- **WHEN** the tool is invoked twice against the same cached catalog
- **THEN** both responses list the same candidates in the same order, grouped by category and ordered by id within each category

### Requirement: The candidate set is complete and unfiltered by relevance

The response SHALL contain every document the document service reports as a skill candidate, with no omissions and no duplicates. The tool SHALL NOT omit a candidate on the basis of its category, tags, subject matter, or any judgement about whether it applies to the caller's codebase. Relevance filtering SHALL be left entirely to the calling agent.

#### Scenario: Every eligible document appears once

- **WHEN** the catalog holds eligible documents across all three categories
- **THEN** the response contains one candidate per eligible document and no duplicates

#### Scenario: No subject-matter filtering is applied

- **WHEN** the catalog contains documents on unrelated subjects
- **THEN** all of them are returned as candidates, with none suppressed by the tool

### Requirement: The instructions direct the agent to judge relevance against its own environment

The response SHALL carry instructions telling the calling agent to inspect the development environment it is working in before writing anything, and to skip candidates that do not apply to it. The instructions SHALL state that a skill for a standard the codebase cannot exercise is redundant and SHALL be omitted rather than written. They SHALL also tell the agent to skip documents describing the expected shape of a document rather than a standard, and to treat a `draft` candidate as provisional rather than authoritative.

#### Scenario: Relevance assessment is instructed

- **WHEN** the response is inspected
- **THEN** it instructs the agent to examine the current development environment and to skip candidates irrelevant to it

#### Scenario: Redundant skills are discouraged explicitly

- **WHEN** the instructions are inspected
- **THEN** they state that a skill for a standard the codebase cannot exercise should not be written

#### Scenario: Templates are excluded by instruction

- **WHEN** the instructions are inspected
- **THEN** they tell the agent to skip candidates that describe how to author a document rather than stating a standard

#### Scenario: Draft status is qualified

- **WHEN** the instructions are inspected
- **THEN** they tell the agent that a `draft` candidate is provisional and should be treated accordingly

### Requirement: The instructions state what every generated skill must contain

The instructions SHALL require each generated skill to carry: an identifier derived from the document; a concise description stating the circumstances under which the skill applies; content distilled from the document that reflects its actual guidance; and a back-reference naming this MCP server, its retrieve-by-id tool, and the source document's `id`. The instructions SHALL state that the back-reference exists so a later agent can retrieve the complete document when the distilled content is insufficient.

#### Scenario: The four required elements are stated

- **WHEN** the instructions are inspected
- **THEN** they require an identifier, a concise description, distilled content, and a back-reference in every generated skill

#### Scenario: The description is trigger-oriented

- **WHEN** the instructions describe the skill description
- **THEN** they require it to state when the skill applies, not merely what the document is about

#### Scenario: The back-reference is actionable

- **WHEN** the instructions describe the back-reference
- **THEN** they require it to name this MCP server, the retrieve-by-id tool, and the document's `id`
- **AND** state that it exists so the complete document can be retrieved on demand

#### Scenario: Content must reflect the document

- **WHEN** the instructions describe the skill content
- **THEN** they require it to reflect the source document's guidance rather than restate its title or description

### Requirement: The instructions prescribe content, not format

The instructions SHALL NOT prescribe a file format, file extension, frontmatter schema, serialisation, or directory location for a generated skill. They SHALL state that the encoding and placement are the consuming client's own convention, while the required content elements are not optional.

#### Scenario: No format is imposed

- **WHEN** the instructions are inspected
- **THEN** they name no file format, extension, frontmatter schema, or directory path

#### Scenario: Encoding is delegated

- **WHEN** the instructions are inspected
- **THEN** they state that the skill's encoding and location follow the consuming client's own convention

#### Scenario: Content requirements remain mandatory

- **WHEN** the instructions delegate the format
- **THEN** they still state that the required content elements are mandatory

### Requirement: The instructions direct the agent to retrieve bodies for kept candidates

The instructions SHALL tell the agent to obtain a document's full markdown through the retrieve-by-id tool for each candidate it decides to keep, and SHALL state that the response contains no document bodies. They SHALL NOT direct the agent to retrieve bodies for candidates it has discarded.

#### Scenario: Retrieval is instructed for kept candidates

- **WHEN** the instructions are inspected
- **THEN** they tell the agent to call the retrieve-by-id tool for each candidate it keeps

#### Scenario: Discarded candidates are not fetched

- **WHEN** the instructions are inspected
- **THEN** they do not direct the agent to retrieve documents it has decided to skip

### Requirement: An empty candidate set is a success and an unloaded catalog is a failure

If the catalog has loaded and yields no eligible candidates, the tool SHALL return an empty candidate set as a successful result, with a response stating there is nothing to generate. If no catalog has ever loaded, the tool SHALL report a tool error identifying the catalog as unavailable, and SHALL NOT return an empty candidate set. If a catalog is cached but its most recent refresh failed, the tool SHALL succeed over the cached catalog.

#### Scenario: No eligible candidates

- **WHEN** the catalog has loaded and every document is excluded from the candidate set
- **THEN** the tool returns an empty candidate set as a successful result, stating there is nothing to generate

#### Scenario: The catalog has never loaded

- **WHEN** the first catalog load has failed and nothing is cached
- **THEN** the tool reports an error stating the catalog is unavailable, and does not return an empty candidate set

#### Scenario: Recommending over a stale catalog

- **WHEN** the cached catalog is past its cache window and the refresh attempt has failed
- **THEN** the tool succeeds and returns candidates from the cached catalog

### Requirement: The server generates, stores, and tracks nothing

The tool SHALL be a read of the cached catalog combined with fixed instruction text. It SHALL NOT write any file, SHALL NOT retain any record of what it recommended or of any skill a caller generated, and SHALL NOT vary its response based on previous invocations.

#### Scenario: Two invocations are identical

- **WHEN** the tool is invoked twice against the same cached catalog
- **THEN** the second response is identical to the first

#### Scenario: Nothing is persisted

- **WHEN** the tool has been invoked
- **THEN** no file is written and no record of the invocation or of any generated skill is retained

### Requirement: The recommendation tool holds no selection logic

The tool class SHALL live in the host project's `Tools` folder, SHALL be registered at composition time in `Program.cs`, and SHALL reach documents only through the Docs project's document service interface. It SHALL apply no eligibility rule, no filtering, and no ordering of its own; it SHALL call the service for the candidate set, project the six fields, and combine them with the instruction text.

#### Scenario: The tool is registered at the seam

- **WHEN** `Program.cs` is inspected
- **THEN** the recommendation tool is registered alongside the other document tools, with no other host file changed to accommodate it

#### Scenario: The tool delegates

- **WHEN** the tool class is inspected
- **THEN** it contains no status comparison, no exclusion rule, and no sort over documents, and its only document dependency is the document service interface
