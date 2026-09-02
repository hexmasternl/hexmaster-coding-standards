## ADDED Requirements

### Requirement: Solution and project layout

The repository SHALL host a single .NET solution file at the repository root. The solution SHALL contain an ASP.NET Core host project at `src/HexMaster.CodingStandards.Mcp`, which serves content over the MCP protocol, a class library at `src/HexMaster.CodingStandards.Docs`, which owns document access, and a unit test project at `tests/HexMaster.CodingStandards.Docs.Tests`. The host project SHALL reference the Docs project; the Docs project SHALL NOT reference the host project. The SDK version SHALL be pinned in a `global.json` at the repository root.

#### Scenario: Solution restores and builds

- **WHEN** `dotnet build` runs at the repository root
- **THEN** all three projects build with no errors and no warnings

#### Scenario: The dependency direction is one-way

- **WHEN** the project references are inspected
- **THEN** the host references the Docs project, and the Docs project references neither the host nor ASP.NET Core hosting packages

### Requirement: Unit tests run on xUnit v3

The test project SHALL use xUnit v3 and SHALL reference the Docs project. It SHALL NOT reference the host project, and its tests SHALL NOT require network access.

#### Scenario: Tests run

- **WHEN** `dotnet test` runs at the repository root
- **THEN** the xUnit v3 test project is discovered and its tests pass

#### Scenario: Tests are offline

- **WHEN** the test suite runs with no network connectivity
- **THEN** all tests still pass

### Requirement: MCP transport over HTTP

The host SHALL expose the Model Context Protocol over HTTP transport in stateless mode, with tool registration performed at composition time in `Program.cs`. The host SHALL NOT enforce HTTPS redirection in-process.

#### Scenario: MCP endpoint responds

- **WHEN** an MCP client performs protocol initialization against the server's MCP endpoint over HTTP
- **THEN** the server completes initialization and reports its server information

#### Scenario: Requests are not tied to a replica

- **WHEN** two consecutive MCP requests from one client are handled by different replicas
- **THEN** both succeed, because no per-session state is held server-side

#### Scenario: Plain HTTP behind a TLS-terminating proxy

- **WHEN** the server receives a plain HTTP request forwarded by the ingress
- **THEN** it handles the request and does not issue an HTTPS redirect

### Requirement: Tool registration seam

The host SHALL contain a `Tools` folder that holds MCP tool classes, and the sample `RandomNumberTools` from the project template SHALL NOT be present. Adding a tool SHALL require adding a class in that folder and registering it in `Program.cs`, with no other file changed.

#### Scenario: No sample tools are exposed

- **WHEN** an MCP client lists the server's tools
- **THEN** no random-number tool is present

#### Scenario: The seam is reachable

- **WHEN** a developer adds a tool class to the `Tools` folder and registers it at composition time
- **THEN** the tool appears in the client's tool listing without further wiring

### Requirement: The document service is registered for dependency injection

The Docs project SHALL provide a single dependency-injection registration method that wires the document service, its GitHub client, and its background refresh, and the host SHALL call that method in `Program.cs`. Adding or changing a Docs project dependency SHALL NOT require an edit to the host.

#### Scenario: One call wires the document service

- **WHEN** `Program.cs` is inspected
- **THEN** document access is wired by a single registration call exposed by the Docs project

#### Scenario: The service is resolvable

- **WHEN** the host starts
- **THEN** the document service and its background refresh resolve from the container without error

### Requirement: Health endpoint

The host SHALL expose an unauthenticated `GET /health` endpoint that returns HTTP 200 when the process can serve requests and the document service has content loaded. It SHALL report unhealthy when the document service has never successfully loaded content, and SHALL remain healthy when a later refresh fails but cached content is still being served.

#### Scenario: Server is healthy

- **WHEN** `GET /health` is requested against a running server whose document service has loaded content
- **THEN** the response status is 200

#### Scenario: Content has never loaded

- **WHEN** the document service's first content load has failed
- **THEN** `GET /health` reports an unhealthy status rather than 200

#### Scenario: A later refresh fails

- **WHEN** a refresh fails while previously loaded content is still cached
- **THEN** `GET /health` still returns 200

### Requirement: Content source configuration

The host SHALL expose the document service's settings — repository owner, repository name, ref, refresh interval, and optional access token — through configuration, with defaults that work for the public repository and no token. Every setting SHALL be overridable by environment variable, and the access token SHALL NOT be committed to the repository or written to logs.

#### Scenario: Defaults work with no configuration

- **WHEN** the container starts with no content-source configuration supplied
- **THEN** the service targets `hexmasternl/hexmaster-coding-standards` at `main` with the default refresh interval

#### Scenario: Settings are overridden per environment

- **WHEN** the ref and refresh interval are supplied as environment variables
- **THEN** the service uses those values

#### Scenario: The token stays secret

- **WHEN** an access token is configured and the application logs its startup configuration
- **THEN** the token value does not appear in any log output, and no token is present in committed files

### Requirement: Containerised build

The repository SHALL contain a `Dockerfile` that builds the host project in a multi-stage build, produces a framework-dependent `linux-x64` application on a chiselled ASP.NET runtime base image, runs as a non-root user, and listens on port 8080. The image SHALL NOT contain the `/docs` content, which is fetched at runtime. The host project SHALL NOT be configured for self-contained or single-file publish.

#### Scenario: Image builds and serves

- **WHEN** the image is built from the repository root and run with port 8080 published and outbound network access
- **THEN** `GET /health` on port 8080 returns 200 once content has loaded

#### Scenario: The image carries no content

- **WHEN** the built image is inspected
- **THEN** it contains no copy of the `/docs` tree or `index.json`

#### Scenario: Publish settings are container-appropriate

- **WHEN** the host project file is inspected
- **THEN** it declares no `PublishSingleFile`, `SelfContained`, or `PublishSelfContained` property, and no multi-platform `RuntimeIdentifiers` list
