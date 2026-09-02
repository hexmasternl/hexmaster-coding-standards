# Tell clients how to use the server on connect

## Why

The skill recommendation tool only helps if something calls it, and nothing does. An agent connecting to this server sees four tool descriptions and no reason to think the first thing it should do in a new repository is generate a set of skills from the standards. Tool descriptions answer "what does this do if I call it" — they cannot answer "call this now, before you need it."

MCP has the seam for exactly this: a server returns an `instructions` string during the initialization handshake, and clients typically place it in the model's system prompt. It is the one piece of server-supplied text an agent reads without being asked. That makes it the right and only place to put the bootstrap directive — and it makes brevity a hard constraint, because whatever goes there is paid for in every session, on every connect, whether or not the standards come up.

## What Changes

- Set `ServerInstructions` on the MCP server options so the server returns instructions during initialization.
- The instructions orient the agent in two or three lines — what this server serves — and state how the tools relate as a workflow, without restating any tool's own description.
- The instructions carry the **first-use directive**: in a workspace where these skills have not been generated yet, call the skill recommendation tool, judge each candidate against the repository at hand, and write the skills it keeps into the location its own client uses for skills.
- **The write is the agent's own tool turn.** The server has no filesystem access to the consumer and does not write, propose, or track files. The instructions say so, so an agent does not wait for a write that will never happen.
- The directive is conditional, not unconditional: an agent that finds these skills already present in the workspace does not regenerate them. Instructions are re-sent on every connect and must not produce work on every connect.
- Placement guidance is **client-neutral**: the instructions name the conventional skill locations for widely used agent clients and instruct any client not listed — or one whose convention has moved on — to use its own. They never name one client's path as *the* path.
- The agent states what it is generating and where before writing, because this is a directive that modifies someone's repository as a side effect of connecting to a server.
- The instruction text is capped and reviewed as an interface: the content requirements for an individual skill stay in the recommendation tool's response, which is only paid for when that tool is actually called.

## Capabilities

### Modified Capabilities
- `mcp-server-host`: Adds the initialization instructions to the host's composition surface — that the server returns them, what they must contain (orientation, tool workflow, the conditional first-use directive, client-neutral placement, the statement that the server writes nothing), what they must not contain (duplicated tool descriptions, a single client's path as the only path, any claim the server writes files), and the size budget that keeps them affordable in every session.

### New Capabilities
<!-- None. The host capability already exists. -->

> **Sequencing.** `mcp-server-host` comes from `mcp-server-project-structure`, which must be archived before this delta applies. The directive names the tool built by `mcp-server-recommended-skills-tool`, so the instructions are inaccurate until that change lands — this change must not be applied before it. `docs-serve-document-by-id`, `docs-serve-document-list`, and `docs-serve-by-tag` supply the tools the workflow line refers to; if the instructions are written before all of them land, the workflow line must describe only the tools that exist.

## Impact

- **Code**: `src/HexMaster.CodingStandards.Mcp/Program.cs` sets `ServerInstructions` at composition; the text itself lives beside the recommendation tool's instruction text so the two are reviewed together and cannot drift apart.
- **Tests**: assertions that the instructions are present and non-empty, stay within the size budget, carry the load-bearing directives, name more than one client convention plus a fallback, and make no claim that the server writes files.
- **Client-visible**: this is the first thing the server says that is *unsolicited*. It costs context in every session with this server connected, including sessions that never touch a coding standard — which is why the budget is a requirement rather than a guideline.
- **Behavioural blast radius**: the text causes agents to write files into users' repositories. It changes what happens in other people's working directories with no version bump, no schema change, and no signal to any client — so it is reviewed like an interface, not like a string constant.
- **Staleness exposure**: naming other clients' conventional paths bets on those conventions holding. They move; Cursor's has already moved once. The fallback clause is what keeps a stale mapping from becoming a wrong answer.
- **Docs**: `README.md` gains what the server tells clients on connect and how to opt out by ignoring it; `CLAUDE.md` needs no architecture change.
- **Non-goals**: no configuration to override or suppress the instruction text, no per-client instruction variants negotiated from client info, no MCP prompts primitive, no server-side generation, storage, or tracking of skills, and no mechanism to refresh skills when a standard changes.
