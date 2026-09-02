# Design — Server instructions on connect

## Context

`mcp-server-recommended-skills-tool` builds a tool that hands an agent skill candidates and tells it how to turn them into skills. It has one flaw as shipped: nothing causes it to be called. Tool descriptions are read when a model is choosing a tool for a task it already has. "Generate skills from the coding standards before you start" is not a task any user asks for, so the tool sits unused.

MCP's initialization handshake returns an optional `instructions` string. The C# SDK exposes it as `McpServerOptions.ServerInstructions`, documented as guidance that "helps models use the server effectively" and that "should not duplicate tool, prompt, or resource descriptions already exposed elsewhere" — clients typically inject it as a system message. That last part is the whole design constraint: this text is unsolicited, it is paid for on every connect, and it lands in the model's system prompt in sessions that may never mention a coding standard.

So the question is not "what would be useful to say" — a great deal would be. It is "what is worth permanent residence in every session's context," with everything else deferred to the tool response, which is only paid for when called.

## Goals / Non-Goals

**Goals:**

- The server returns instructions on initialize, so an agent knows what this server is without calling anything.
- A first-use directive that actually fires: call the recommendation tool and write the resulting skills.
- The directive is conditional, so a re-connect does not mean re-generation.
- Placement guidance that works for Claude Code, Copilot, and Cursor, and degrades sensibly for anything else.
- The agent, not the server, performs the write — stated plainly enough that no agent waits on the server to do it.
- Small enough that a user with this server connected does not resent it.

**Non-Goals:**

- Configuration to override, customise, or suppress the text.
- Per-client instruction variants selected from the client info in the handshake.
- The MCP prompts primitive. A prompt is user-invoked; this needs to fire without anyone knowing to ask.
- Server-side skill generation, storage, or tracking — the server has no filesystem access to the consumer and keeps no state.
- Detecting that a standard changed and refreshing generated skills.
- Restating any tool's description.

## Decisions

### The instructions carry the trigger; the tool response carries the content requirements

Two pieces of instruction text now exist, and the split between them is deliberate. The server instructions say **when and where**: this is what the server is, here is how the tools relate, on first use in a workspace generate skills, put them where your client keeps skills. The recommendation tool's response says **what each skill must contain**: identifier, trigger-oriented description, distilled content, back-reference.

The split follows the cost. Server instructions are paid for in every session; the tool response is paid for only when an agent has committed to generating skills. Putting the four content requirements in the system prompt would tax every unrelated conversation to inform one that may never happen. It would also duplicate the tool's own response, which the SDK explicitly warns against.

The two texts live next to each other in the host project so they are reviewed together. Their failure mode is drift — the directive naming a workflow the tool no longer describes — and proximity is the cheap defence.

### A hard size budget, specified as a number

The instructions must not exceed 2,000 characters. A budget stated as "keep it brief" is not a budget; it loses every argument with a good sentence someone wants to add. A number is testable, shows up in review as a failure rather than an opinion, and forces the next person who wants to add a paragraph to decide what comes out.

2,000 characters is enough for orientation, a workflow line, the conditional directive, a client mapping with a fallback, and the statement that the server does not write — with nothing left over, which is the point.

### The directive is conditional on the workspace, not on the connection

Instructions are re-sent on every initialize. An unconditional "generate skills" would mean regenerating on every new session, overwriting hand-edited skills and burning tokens for no change.

So the directive is phrased as a check: if skills sourced from this server are not already present in the workspace, generate them. An agent can answer that by looking at its own skills location for skills carrying this server's back-reference — which the recommendation tool already requires every generated skill to include. The back-reference was added so a later agent could retrieve the full document; it doubles as the marker that makes the bootstrap idempotent, at no extra cost.

*Alternative considered.* Server-side tracking of which workspaces have been bootstrapped. The server has no notion of a workspace, no identity for one, no storage, and no business knowing. The filesystem the agent is already looking at is the authoritative record.

### The write is the agent's own tool turn, and the instructions say so

The server returns text. It has no access to the consumer's filesystem and cannot write, stage, or propose a file. The instructions state this explicitly rather than leaving it implied, because the failure it prevents is specific: an agent that reads "the server generates skills" and waits for files to appear, or reports to the user that skills were installed when nothing was written.

### Client-neutral placement, with a fallback that carries the weight

The instructions name conventional skill locations for widely used clients and then say: if your client is not listed, or its convention has changed, use your own convention.

The concrete mapping intended at implementation time — to be re-verified against each client's current documentation before it is written, not copied from here:

| Client | Conventional location | Shape |
|---|---|---|
| Claude Code | `.claude/skills/<name>/SKILL.md` | YAML frontmatter with `name` and `description`, markdown body |
| GitHub Copilot | `.github/instructions/<name>.instructions.md` | frontmatter with `applyTo`, markdown body |
| Cursor | `.cursor/rules/<name>.mdc` | frontmatter with `description` and `globs`, markdown body |
| Anything else | the client's own convention | the client's own convention |

Naming other people's paths is a bet that their conventions hold, and that bet loses eventually — Cursor's convention has already moved once, from a single `.cursorrules` file to `.cursor/rules/*.mdc`. The mapping is still worth having: a concrete path is the difference between an agent that writes a usable file and one that invents a plausible location. But the fallback is what keeps a stale row from becoming a wrong answer, so it is a requirement rather than a courtesy, and the mapping must never be phrased as exhaustive.

The instructions also say to write only into the client's conventional skills location. An instruction that can put files anywhere in a repository is a worse trade than one that occasionally puts them nowhere.

### The agent says what it is generating before it writes

Connecting to a server should not silently add files to someone's repository. The instructions tell the agent to state what it is generating and where, before writing.

Deliberately a statement, not an approval gate per file: a directive that demands confirmation for each of a dozen skills will be abandoned halfway, and the user ends up with a partial skill set and no idea which half. One line up front, then the work, leaves the user informed and able to stop it — which is the actual goal. The client's own permission model still applies to every write on top of this.

### Fixed text, no configuration

No setting overrides or suppresses the instructions. A configuration surface for prompt text invites per-deployment divergence in the one thing that most needs to be consistent, and an operator who does not want the behaviour has a simpler remedy: agents are free to ignore instructions, and the directive is conditional anyway. If a real need appears — an organisation that wants the server without the bootstrap — a single boolean to suppress the directive is additive and cheap. Not now.

## Risks / Trade-offs

- **Every session pays for this, including the ones that never touch a standard.** → The 2,000-character budget is a tested requirement, and the expensive half — the per-skill content requirements — stays in the tool response where it is paid for on use. This is the cost of the feature existing at all; the alternative is a tool nothing calls.
- **The text writes files into other people's repositories, with no version and no signal.** A wording change here changes behaviour everywhere on the next connect. → Reviewed as an interface; tests assert the load-bearing elements are present rather than pinning exact wording, so editorial improvement stays cheap while removing a directive fails the build.
- **The client mapping will go stale.** → The fallback clause is a requirement, the mapping is never phrased as exhaustive, and the implementation task says to re-verify each path against current documentation rather than trusting the table above. A stale row degrades to "use your own convention"; it does not become an instruction to write somewhere wrong.
- **The idempotence check is heuristic.** An agent that looks in the wrong place, or a user who moved their skills, gets a regeneration. → Bounded harm — duplicate or overwritten generated skills, not lost work — and the marker is the back-reference every generated skill already carries. Reliable idempotence would need state the server has no business holding.
- **An agent may follow the directive at an unwelcome moment**, generating a dozen skills when the user asked something unrelated. → The pre-write statement gives the user a place to stop it, and the directive is scoped to first use in a workspace. This is the residual cost of an unsolicited instruction, and it is why the directive is conditional rather than standing.
- **Ordering against four in-flight changes.** The directive names a tool that does not exist yet, and the workflow line names three more. → Recorded in the proposal: this change must land after `mcp-server-recommended-skills-tool`, and the workflow line describes only tools that exist when it is written.
