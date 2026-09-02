## ADDED Requirements

### Requirement: A tool serves a document by id

The MCP host SHALL expose a tool that takes a single required string parameter naming a catalog `id` and returns that document's metadata together with its full markdown body. The tool SHALL be discoverable in the server's tool listing, and its name, parameter, and description SHALL state that the id comes from the document catalog, so a client can tell what value to supply without reading the source.

#### Scenario: The tool is listed

- **WHEN** an MCP client lists the server's tools
- **THEN** the retrieve-by-id tool is present with a description of what it returns
- **AND** its input schema declares exactly one required string parameter for the document id

#### Scenario: A known document is returned

- **WHEN** the tool is invoked with an id present in the catalog
- **THEN** the result carries that document's `id`, `title`, `description`, `category`, `status`, and `tags`, together with its full markdown body
- **AND** the result is not flagged as an error

#### Scenario: The body is returned whole

- **WHEN** a document whose markdown body spans many lines is requested
- **THEN** the returned body is the document's complete text, neither truncated nor summarised

### Requirement: An unknown id is reported as a tool error

When no catalog entry has the requested id, the tool SHALL return a result flagged as an error whose message names the requested id and states that no such document is catalogued. It SHALL NOT return an empty success, SHALL NOT return a different document, and SHALL NOT fall back to a partial, fuzzy, or case-insensitive match.

#### Scenario: The id is not in the catalog

- **WHEN** the tool is invoked with an id absent from the catalog
- **THEN** the result is flagged as an error naming the requested id
- **AND** no document content is returned

#### Scenario: No fallback match is made

- **WHEN** the requested id differs from a catalogued id only by case or by a trailing segment
- **THEN** the result is still an error, and the near-matching document is not returned

#### Scenario: A blank id is rejected

- **WHEN** the tool is invoked with an empty or whitespace-only id
- **THEN** the result is flagged as an error stating that an id is required
- **AND** no request is made to GitHub

### Requirement: An unavailable body is reported distinctly from an unknown id

When the requested id is catalogued but its body cannot be obtained — the file is missing at the configured ref, the request is rate-limited, or GitHub fails or is unreachable — the tool SHALL return a result flagged as an error whose message names the id and makes clear that the document exists but its content could not be retrieved. That message SHALL be distinguishable from the unknown-id message, and SHALL NOT expose the configured access token, request headers, or a raw stack trace.

#### Scenario: The catalogued file is missing at the ref

- **WHEN** a catalogued entry's `path` names a file GitHub does not have at the configured ref
- **THEN** the result is an error stating the document is catalogued but its content is unavailable
- **AND** that message differs from the message returned for an unknown id

#### Scenario: GitHub is unreachable

- **WHEN** the body fetch fails because GitHub cannot be reached
- **THEN** the result is an error indicating the content could not be retrieved
- **AND** the failure is logged with the id and the underlying cause

#### Scenario: The failure leaks no secrets

- **WHEN** any body-fetch failure is reported to the client
- **THEN** the message contains no access token, no request headers, and no stack trace

### Requirement: Failures do not take down the server

A failure to serve one document SHALL NOT terminate the host, mark the replica unhealthy, or affect a subsequent request for a different document. `GET /health` SHALL continue to report healthy for as long as the catalog has loaded, regardless of how many body fetches have failed.

#### Scenario: A later request succeeds

- **WHEN** one invocation fails because its body could not be fetched and a second invocation requests a different, available document
- **THEN** the second invocation succeeds

#### Scenario: Health is unaffected

- **WHEN** every body fetch since startup has failed but the catalog loaded successfully
- **THEN** `GET /health` still returns 200

### Requirement: The tool holds no document-access logic

The tool class SHALL live in the host project's `Tools` folder, SHALL be registered at composition time in `Program.cs`, and SHALL reach documents only through the Docs project's document service interface. It SHALL contain no HTTP client, no GitHub-specific code, and no caching of its own.

#### Scenario: The tool is registered at the seam

- **WHEN** `Program.cs` is inspected
- **THEN** the tool is registered alongside the other MCP tools, with no other host file changed to accommodate it

#### Scenario: The tool delegates

- **WHEN** the tool class is inspected
- **THEN** its only document dependency is the document service interface, and it performs no HTTP request, no GitHub URL construction, and no caching
