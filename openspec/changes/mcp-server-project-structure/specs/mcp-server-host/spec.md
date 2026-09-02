## ADDED Requirements

### Requirement: Solution and project layout

The repository SHALL host a single .NET solution file at the repository root. The solution SHALL contain an ASP.NET Core host project at `src/HexMaster.CodingStandards.Mcp`, a class library at `src/HexMaster.CodingStandards.Core`, and a unit test project at `tests/HexMaster.CodingStandards.Core.Tests`. The host project SHALL reference the class library; the class library SHALL NOT reference the host project. The SDK version SHALL be pinned in a `global.json` at the repository root.

#### Scenario: Solution restores and builds

- **WHEN** `dotnet build` runs at the repository root
- **THEN** all three projects build with no errors and no warnings

#### Scenario: Tests run

- **WHEN** `dotnet test` runs at the repository root
- **THEN** the test project is discovered and its tests pass

#### Scenario: Domain logic is testable without the web host

- **WHEN** the test project's dependencies are inspected
- **THEN** it references `HexMaster.CodingStandards.Core` and does not reference the host project

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

#### Scenario: The seam is documented and reachable

- **WHEN** a developer adds a tool class to the `Tools` folder and registers it at composition time
- **THEN** the tool appears in the client's tool listing without further wiring

### Requirement: Health endpoint

The host SHALL expose an unauthenticated `GET /health` endpoint that returns HTTP 200 when the process is able to serve requests, and SHALL report unhealthy when the configured content root is not readable.

#### Scenario: Server is healthy

- **WHEN** `GET /health` is requested against a running server whose content root is readable
- **THEN** the response status is 200

#### Scenario: Content root is missing

- **WHEN** the configured content root does not exist on disk
- **THEN** `GET /health` reports an unhealthy status rather than 200

### Requirement: Content root configuration

The host SHALL read the location of the document content root from configuration, defaulting to a path valid for the container image. The configured value SHALL be overridable by environment variable so the same build serves content from a different location locally and in the container.

#### Scenario: Default configuration in the container

- **WHEN** the container starts with no content-root override
- **THEN** the server resolves the content root to the documents baked into the image

#### Scenario: Local override

- **WHEN** the content root is overridden by environment variable to the repository's `docs` folder
- **THEN** the server reads the catalog from that folder

### Requirement: Document reads are confined to the content root

Any resolution of a catalog `path` to a file on disk SHALL be confined to the configured content root. A path that escapes the content root, whether by traversal segments, an absolute path, or a symbolic link, SHALL be rejected without reading the target.

#### Scenario: Traversal segments are rejected

- **WHEN** a catalog entry's `path` resolves outside the content root, such as `../../etc/passwd`
- **THEN** resolution fails and no file outside the content root is read

#### Scenario: Absolute paths are rejected

- **WHEN** a catalog entry's `path` is an absolute filesystem path
- **THEN** resolution fails

#### Scenario: A legitimate document resolves

- **WHEN** a catalog entry's `path` names a markdown file inside a category folder under the content root
- **THEN** resolution succeeds and returns that file's full path

### Requirement: Containerised build

The repository SHALL contain a `Dockerfile` that builds the host project in a multi-stage build, produces a framework-dependent `linux-x64` application on a chiselled ASP.NET runtime base image, copies the `/docs` content into the image, runs as a non-root user, and listens on port 8080. The host project SHALL NOT be configured for self-contained or single-file publish.

#### Scenario: Image builds and serves

- **WHEN** the image is built from the repository root and run with port 8080 published
- **THEN** `GET /health` on port 8080 returns 200

#### Scenario: Content ships in the image

- **WHEN** the built image is inspected
- **THEN** the `/docs` catalog and category folders are present at the default content root

#### Scenario: Publish settings are container-appropriate

- **WHEN** the host project file is inspected
- **THEN** it declares no `PublishSingleFile`, `SelfContained`, or `PublishSelfContained` property, and no multi-platform `RuntimeIdentifiers` list
