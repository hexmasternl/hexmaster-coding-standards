# Tasks — Server instructions on connect

> Prerequisite: `mcp-server-project-structure` must be archived (it owns `mcp-server-host`), and `mcp-server-recommended-skills-tool` must be implemented — the directive names its tool, so this change must not land first. The workflow line refers to the discovery and retrieval tools; describe only the tools that actually exist when the text is written.

## 1. Verify client conventions before writing anything

- [ ] 1.1 Confirm Claude Code's current skill location and file shape against its own documentation, rather than assuming `.claude/skills/<name>/SKILL.md` with `name`/`description` frontmatter still holds
- [ ] 1.2 Confirm GitHub Copilot's current custom-instructions location and file shape, including whether per-file `applyTo` frontmatter is still the mechanism
- [ ] 1.3 Confirm Cursor's current rules location and file shape, noting that this convention has already moved once from a single `.cursorrules` file
- [ ] 1.4 Record what was verified and when, so the next person editing the text knows how old the mapping is

## 2. Write the instruction text

- [ ] 2.1 Place the text in the host project beside the recommendation tool's instruction text, so the two are reviewed together and cannot drift apart
- [ ] 2.2 Open with what the server serves, in no more than two sentences
- [ ] 2.3 State how the tools relate as a workflow — how a document is discovered and how its full text is obtained — without restating any tool's description, parameters, or return shape
- [ ] 2.4 Write the first-use directive conditioned on skills from this server not already being present in the workspace, directing the agent to call the recommendation tool, judge candidates against the repository at hand, and write only what it keeps
- [ ] 2.5 State that skills already present are not regenerated, and phrase the directive so it does not read as an action for every connection or session
- [ ] 2.6 State that the server returns content only, has no access to the consumer's filesystem, and that the agent performs the writes
- [ ] 2.7 Write the client mapping from the values verified in group 1, followed by a fallback instructing any unlisted client, or one whose convention has changed, to use its own — and do not phrase the list as exhaustive
- [ ] 2.8 Direct the agent to write only within its client's conventional skills location
- [ ] 2.9 Direct the agent to state what it is generating and where before writing, without requiring approval for each individual skill
- [ ] 2.10 Keep the per-skill content requirements out of this text — they belong in the recommendation tool's response
- [ ] 2.11 Trim to fit within 2,000 characters, deciding what comes out rather than letting the text grow to accommodate an addition

## 3. Wire it into composition

- [ ] 3.1 Set `ServerInstructions` on the MCP server options in `Program.cs` at composition time, alongside the existing transport and tool registration
- [ ] 3.2 Confirm the text is fixed: it does not vary by client, by request, or between invocations, and no configuration overrides or suppresses it
- [ ] 3.3 Verify no other host file changed to accommodate it

## 4. Tests

- [ ] 4.1 Test the composed instructions are present and non-empty
- [ ] 4.2 Test the length does not exceed 2,000 characters
- [ ] 4.3 Test the load-bearing elements are present — the first-use directive naming the recommendation tool, its conditional framing, the statement that the agent performs the write, the client mapping, the fallback clause, the confine-to-skills-location instruction, and the state-before-writing instruction — asserting on presence, not exact wording
- [ ] 4.4 Test that more than one client convention is named and that no single client's location is presented as the only destination
- [ ] 4.5 Test the text contains no claim that the server writes, installs, stages, or tracks files
- [ ] 4.6 Test the per-skill content requirements appear in the recommendation tool's response and not in the instructions
- [ ] 4.7 Confirm rewording a directive without losing its meaning leaves the suite passing, so editorial improvement stays cheap

## 5. End-to-end verification and documentation

- [ ] 5.1 Connect a real MCP client and confirm the instructions arrive in the initialization result and reach the model's context
- [ ] 5.2 In a fresh .NET repository with no generated skills, confirm the agent follows the directive: calls the recommendation tool, states what it will generate, skips irrelevant candidates, and writes skills into the client's conventional location
- [ ] 5.3 Reconnect in the same workspace and confirm the agent does not regenerate the skills it already wrote
- [ ] 5.4 Repeat 5.2 with a second client whose convention differs, and confirm the skills land in that client's location rather than the first one's
- [ ] 5.5 Confirm no file is written outside the client's conventional skills location
- [ ] 5.6 Document in `README.md` what the server tells clients on connect, that the agent performs the writes, and that ignoring the directive is a supported outcome
- [ ] 5.7 Run `dotnet build` and `dotnet test` from the repository root with no network access and confirm a clean, warning-free build and a passing offline suite
