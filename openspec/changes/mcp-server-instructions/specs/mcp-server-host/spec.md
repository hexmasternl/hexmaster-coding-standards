## ADDED Requirements

### Requirement: The server returns instructions during initialization

The host SHALL supply server instructions at composition time, so that the initialization result carries a non-empty instructions string. The instructions SHALL be fixed text that does not vary by client, by request, or between invocations.

#### Scenario: Instructions reach the client

- **WHEN** an MCP client completes protocol initialization against the server
- **THEN** the initialization result carries a non-empty instructions string

#### Scenario: Instructions are stable

- **WHEN** two clients initialize against the server
- **THEN** both receive identical instructions

#### Scenario: Instructions are composed in the host

- **WHEN** `Program.cs` is inspected
- **THEN** the server instructions are supplied at composition time alongside the other MCP server options

### Requirement: The instructions orient the agent and relate the tools as a workflow

The instructions SHALL state what the server serves, and SHALL describe how its tools relate as a workflow — how a document is discovered and how its full text is obtained. They SHALL NOT restate any individual tool's own description, parameters, or return shape, which the client already has from the tool listing.

#### Scenario: The server is identified

- **WHEN** the instructions are inspected
- **THEN** they state that the server serves this organisation's coding standards and guidelines

#### Scenario: The tools are related as a workflow

- **WHEN** the instructions are inspected
- **THEN** they describe how documents are discovered and how a document's full text is obtained

#### Scenario: Tool descriptions are not duplicated

- **WHEN** the instructions are compared with the tool listing
- **THEN** they do not restate any tool's description, parameter list, or return shape

### Requirement: The instructions carry a conditional first-use skill directive

The instructions SHALL direct the agent, when working in a workspace where skills sourced from this server are not already present, to call the skill recommendation tool, judge each candidate against the repository at hand, and write the skills it keeps. The directive SHALL be conditional on the workspace's existing state, and SHALL direct the agent not to regenerate skills that are already present. It SHALL NOT be phrased as an action to take on every connection or in every session.

#### Scenario: The directive names the recommendation tool

- **WHEN** the instructions are inspected
- **THEN** they direct the agent to call the skill recommendation tool on first use in a workspace

#### Scenario: The directive is conditional

- **WHEN** the instructions are inspected
- **THEN** the directive is conditioned on skills from this server not already being present in the workspace

#### Scenario: Regeneration is prevented

- **WHEN** an agent reconnects to the server in a workspace where these skills already exist
- **THEN** the instructions direct it not to generate them again

#### Scenario: Relevance judgement is preserved

- **WHEN** the instructions describe the directive
- **THEN** they direct the agent to judge candidates against the repository at hand rather than write a skill for every candidate

### Requirement: The instructions state that the agent performs the write

The instructions SHALL state that the server returns content only, that it has no access to the consumer's filesystem, and that writing the skills is the agent's own action. They SHALL NOT claim or imply that the server creates, installs, stages, or tracks any file.

#### Scenario: The write is attributed to the agent

- **WHEN** the instructions are inspected
- **THEN** they state that the agent performs the file writes

#### Scenario: No filesystem claim is made

- **WHEN** the instructions are inspected
- **THEN** they contain no claim that the server writes, installs, stages, or tracks files

#### Scenario: An agent does not wait on the server

- **WHEN** an agent follows the directive
- **THEN** the instructions give it no reason to wait for the server to produce files

### Requirement: Placement guidance is client-neutral and carries a fallback

The instructions SHALL name conventional skill locations for more than one widely used agent client, and SHALL instruct any client not named — or one whose convention has changed — to use its own convention instead. The guidance SHALL NOT present a single client's location as the only location, and SHALL NOT be phrased as an exhaustive list. The instructions SHALL direct the agent to write only within its client's conventional skills location.

#### Scenario: More than one client convention is named

- **WHEN** the instructions are inspected
- **THEN** they name conventional skill locations for more than one agent client

#### Scenario: A fallback is provided

- **WHEN** the instructions are inspected
- **THEN** they instruct a client that is not named, or whose convention has changed, to use its own convention

#### Scenario: No client's path is presented as the only path

- **WHEN** the instructions are inspected
- **THEN** no single client's location is presented as the required destination, and the list is not phrased as exhaustive

#### Scenario: Writes are confined to the skills location

- **WHEN** the instructions describe where to write
- **THEN** they direct the agent to write only within its client's conventional skills location

### Requirement: The agent states what it will generate before writing

The instructions SHALL direct the agent to state what skills it is generating and where it is writing them before it writes. They SHALL NOT require the agent to seek approval for each individual skill.

#### Scenario: The user is told before files appear

- **WHEN** the instructions describe the directive
- **THEN** they require the agent to state what it is generating and where, before writing

#### Scenario: No per-file approval gate

- **WHEN** the instructions are inspected
- **THEN** they do not require confirmation for each individual skill

### Requirement: The instructions stay within a size budget

The instructions SHALL NOT exceed 2,000 characters. Content that is only needed once an agent has decided to generate skills — in particular the required content of an individual skill — SHALL live in the recommendation tool's response rather than in the instructions.

#### Scenario: The budget is enforced

- **WHEN** the instructions are measured
- **THEN** their length does not exceed 2,000 characters

#### Scenario: Per-skill content requirements are not duplicated

- **WHEN** the instructions are compared with the recommendation tool's response
- **THEN** the required content of an individual skill is stated in the tool's response and not repeated in the instructions

### Requirement: The instruction text is reviewed as an interface

The instruction text SHALL live in the host project beside the recommendation tool's instruction text, so the two are reviewed together. Tests SHALL assert that its load-bearing elements are present rather than pinning its exact wording, so that removing a directive fails the build while rewording one does not.

#### Scenario: The two texts sit together

- **WHEN** the host project is inspected
- **THEN** the server instruction text and the recommendation tool's instruction text are located together

#### Scenario: Removing a directive fails the build

- **WHEN** the first-use directive, the client-neutral placement guidance, the fallback, or the statement that the agent performs the write is removed
- **THEN** a test fails

#### Scenario: Rewording does not fail the build

- **WHEN** a directive is rephrased without losing its meaning
- **THEN** the tests still pass
