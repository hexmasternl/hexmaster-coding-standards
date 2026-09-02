## ADDED Requirements

### Requirement: Infrastructure is declared in Bicep

All Azure resources for the MCP server SHALL be declared in Bicep under `/infra`. `/infra/main.bicep` SHALL be the single entry point, scoped to a resource group, composing modules under `/infra/modules`. Environment-specific values SHALL live in parameter files under `/infra/params`, one per environment, and SHALL NOT be hard-coded in the modules. A `prod` parameter file targeting `swedencentral` SHALL be provided.

#### Scenario: Templates compile

- **WHEN** `az bicep build` runs against `infra/main.bicep`
- **THEN** it succeeds with no errors and no warnings

#### Scenario: Deployment is validated before it runs

- **WHEN** a what-if deployment runs against the resource group with the `prod` parameter file
- **THEN** it reports the resource changes without error

#### Scenario: Adding an environment requires no template change

- **WHEN** a second parameter file is added under `infra/params`
- **THEN** it can be deployed against `infra/main.bicep` with no edit to `main.bicep` or the modules

### Requirement: Azure resource topology

The deployment SHALL provision a Log Analytics workspace, a Container Apps managed environment wired to that workspace, an Azure Container Registry, a user-assigned managed identity, and a container app running the MCP server. Every resource SHALL carry tags identifying the application and the environment.

#### Scenario: Resources are created

- **WHEN** `infra/main.bicep` is deployed to an empty resource group
- **THEN** the workspace, managed environment, registry, user-assigned identity, and container app all exist

#### Scenario: Logs reach the workspace

- **WHEN** the container app writes to standard output
- **THEN** those entries are queryable in the Log Analytics workspace

#### Scenario: Resources are tagged

- **WHEN** the deployed resources are inspected
- **THEN** each carries the application and environment tags

### Requirement: The container app scales to zero

The container app SHALL be configured with a minimum replica count of zero and a parameterised maximum replica count, scaling on inbound HTTP traffic. It SHALL run on a consumption workload profile.

#### Scenario: Idle app costs no compute

- **WHEN** the app receives no traffic for longer than its scale-in window
- **THEN** its replica count drops to zero

#### Scenario: A request wakes the app

- **WHEN** an MCP request arrives while the app has zero replicas
- **THEN** a replica starts and the request is served

#### Scenario: Scale-out is bounded

- **WHEN** concurrent traffic exceeds the scale rule threshold
- **THEN** replicas are added up to the configured maximum and no further

### Requirement: Ingress terminates TLS externally

The container app SHALL expose external ingress on the container's HTTP port, SHALL reject insecure connections at the edge, and SHALL publish an HTTPS fully qualified domain name as a deployment output.

#### Scenario: Server is reachable over HTTPS

- **WHEN** an MCP client connects to the ingress FQDN over HTTPS
- **THEN** protocol initialization succeeds

#### Scenario: Plain HTTP is refused at the edge

- **WHEN** a request is made to the ingress over plain HTTP
- **THEN** the edge does not serve it insecurely

#### Scenario: The endpoint is discoverable after deployment

- **WHEN** the deployment completes
- **THEN** its outputs include the container app's FQDN

### Requirement: Registry access uses managed identity

The container app SHALL pull its image from the container registry using the user-assigned managed identity, granted the `AcrPull` role on that registry by the deployment. Registry administrator credentials SHALL be disabled, and no registry username or password SHALL appear in the templates, parameter files, or app configuration.

#### Scenario: Image is pulled without credentials

- **WHEN** the container app starts a replica
- **THEN** it pulls the image using the managed identity and starts successfully

#### Scenario: No registry secrets exist

- **WHEN** the templates, parameter files, and deployed app configuration are inspected
- **THEN** no registry username or password is present, and the registry's admin user is disabled

### Requirement: The image reference is a deployment parameter

`main.bicep` SHALL accept the container image reference as a parameter, defaulting to a publicly pullable placeholder image so a first deployment into an empty resource group succeeds before any application image exists.

#### Scenario: Bootstrap deployment into an empty resource group

- **WHEN** `main.bicep` is deployed with no image parameter supplied and the registry does not yet hold an image
- **THEN** the deployment succeeds and the container app runs the placeholder image

#### Scenario: Deploying a specific build

- **WHEN** `main.bicep` is deployed with the image parameter set to a registry image tagged with a commit SHA
- **THEN** the container app runs that image

#### Scenario: Rolling back

- **WHEN** `main.bicep` is redeployed with the image parameter set to a previously deployed commit SHA tag
- **THEN** the container app returns to running that earlier image
