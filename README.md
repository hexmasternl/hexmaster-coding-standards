# HexMaster Coding Standards

HexMaster's coding standards and guidelines, served to AI coding agents over the
[Model Context Protocol](https://modelcontextprotocol.io/).

The standards themselves are the markdown in [`docs/`](docs) — architecture decision records,
coding designs, and project structure standards. The MCP server downloads them from this
repository at runtime, so **publishing a standard is a merge to `main`**, not a deployment.

## Setup

The server is hosted, so there is nothing to install, clone, or run: the whole configuration
is one URL.

| | |
| --- | --- |
| **Endpoint** | `https://standards-mcp.hexmaster.nl` |
| **Transport** | MCP over streamable HTTP — no stdio command, no local proxy |
| **Authentication** | None; the standards are public |
| **Health check** | `https://standards-mcp.hexmaster.nl/health` returns `Healthy` |

It runs as an Azure Container App that scales to zero when idle, so the first call after a
quiet period takes a few seconds while a replica starts and loads the catalog. Nothing is
wrong; a client that gives up instantly succeeds on the next try.

Setting it up in a repository is the same three steps in every client — **add the server,
check that it connected, then let it write the skills** — and the third step is the one worth
not skipping. Adding the server makes the standards available *when someone asks*. Generating
the skills is what makes a repository follow them by default, and it is a one-time action per
repository. [Turning the standards into skills](#turning-the-standards-into-skills) describes
what that produces.

### Claude Code

**1. Add the server.** From the repository you want it in:

```powershell
claude mcp add --transport http --scope project hexmaster-coding-standards https://standards-mcp.hexmaster.nl
```

`--scope project` writes `.mcp.json` in the repository root — checked in, so everyone who
clones the repository gets the server. `--scope user` puts it in every project on your
machine instead, and the default `--scope local` keeps it to you in this project only. For a
team standard, `project` is the one to want.

Written by hand, `.mcp.json` at the repository root:

```json
{
  "mcpServers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "https://standards-mcp.hexmaster.nl"
    }
  }
}
```

**2. Check that it connected.** Run `/mcp` in a session: the server should be listed as
connected, offering four tools. A checked-in `.mcp.json` asks each user to approve the
project's servers the first time they open it.

**3. Let it write the skills.** Claude Code puts the server's connect-time instructions in
the system prompt, so a fresh session in a repository carrying no skills from this server
will often do this unprompted. To be explicit about it:

> Set this repository up to follow the HexMaster coding standards.

It calls `recommend_skills`, judges the candidates against what the repository actually is,
fetches only the standards that survive, and writes one skill per kept standard to
`.claude/skills/<name>/SKILL.md`. It says what it is generating before it starts. Review the
files and commit them — they are yours, and the server keeps no record of them.

**Everyday use.** Ask in prose — "what does our ADR say about messaging?", "review this
against our project structure standard" — and the tools get called as needed. Tool calls
prompt for permission the first time; `/permissions` can allowlist
`mcp__hexmaster-coding-standards` if you would rather not be asked again.

### GitHub Copilot

**1. Add the server.** In VS Code, put this in `.vscode/mcp.json` to share it with the
repository:

```json
{
  "servers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "https://standards-mcp.hexmaster.nl"
    }
  }
}
```

The key is `servers`, not `mcpServers` — VS Code and Visual Studio use that spelling. The
**MCP: Add Server…** command does the same thing interactively (choose *HTTP*, paste the URL)
and can write it to your user profile so it applies to every workspace. In **Visual Studio**,
the identical JSON goes in `.mcp.json` beside the `.sln`/`.slnx` — see
[MCP servers in Visual Studio](https://learn.microsoft.com/visualstudio/ide/mcp-servers).

**2. Check that it connected.** **MCP: List Servers** shows each server's state and its
output log, and the `mcp.json` editor has start/stop actions above every entry. Then open
Copilot Chat, switch to **Agent** mode, and look for the four tools in the tools picker.

Agent mode is not optional: Copilot Chat only calls MCP tools there. In Ask mode the server
connects and is then silently never used, which looks like a broken server and is not one.

**3. Let it write the skills.** In Agent mode:

> Call recommend_skills and set this repository up to follow the HexMaster coding standards.

Being explicit matters more here than in Claude Code: a server's connect-time instructions
are guidance, and how much weight a client gives them varies. The skills land in
`.github/instructions/<name>.instructions.md`, each with `applyTo` frontmatter carrying the
globs that decide when Copilot loads it. Review and commit.

**Everyday use.** Agent mode, prose questions. Copilot asks to confirm a tool run the first
time and can remember that answer per tool.

**The coding agent** on github.com is configured separately from the editor, in the
repository's Settings → Copilot → Coding agent → MCP configuration. Same URL, the
`mcpServers` spelling, plus an explicit tool allow-list:

```json
{
  "mcpServers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "https://standards-mcp.hexmaster.nl",
      "tools": ["*"]
    }
  }
}
```

### Cursor

**1. Add the server.** `.cursor/mcp.json` in the repository — checked in, shared with the
team — or `~/.cursor/mcp.json` to have it in every project:

```json
{
  "mcpServers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "https://standards-mcp.hexmaster.nl"
    }
  }
}
```

**2. Check that it connected.** Cursor Settings → **Tools & MCP** lists the server as soon as
the file is saved. Make sure it is enabled and that its four tools are showing; a server that
is listed with no tools has not finished connecting, which on a cold start means trying again
in a few seconds.

**3. Let it write the skills.** In Agent mode:

> Call recommend_skills and set this repository up to follow the HexMaster coding standards.

The rules land in `.cursor/rules/<name>.mdc`, with `description`, `globs`, and `alwaysApply`
frontmatter. The `.mdc` extension is load-bearing: a plain `.md` file in that directory is
ignored. Review and commit.

**Everyday use.** Agent mode calls the tools; ask in prose. Cursor's tool-approval setting
decides whether each call needs a click.

### Any other client

Any client speaking MCP over streamable HTTP connects to the same URL, and the config is one
of two shapes — `mcpServers` (Claude, Cursor, most CLIs) or `servers` (VS Code, Visual
Studio) — with the same object inside. A client that only supports stdio needs a generic
remote-MCP proxy in front of it: this server offers no stdio variant, because there is
nothing to run locally.

The container app's own `*.azurecontainerapps.io` ingress hostname answers identically and is
published in the deployment summary of the most recent
[CD run](../../actions/workflows/cd.yml), which is worth knowing if the custom domain is ever
in doubt.

### When it does not work

| What you see | What it usually is |
| --- | --- |
| The first call hangs for several seconds | Scale-from-zero. Expected — retry rather than reconfigure. |
| The server is listed with zero tools | The connection never completed. Restart the server entry, and open `/health` in a browser to see whether the server or the client is the problem. |
| `/health` says `Unhealthy` | That replica has never loaded the catalog — GitHub unreachable, or rate-limiting the server. It recovers by itself; there is nothing to change client-side. |
| The model answers about the standards without calling anything | Either Copilot or Cursor is not in Agent mode, or it is answering from a generated skill — which is what they are for, and correct. |
| Nothing happens on first use in a repository | The connect-time directive is guidance, not protocol. Ask for the setup explicitly, as in step 3. |
| `get_document` reports an unknown id | Ids are exact and case-sensitive. Call `list_documents` or `find_documents_by_tag` first and use the id it returns. |
| A standard changed but the answer is stale | Both caches are 30 minutes, so nothing served is older than that. |

## Using the server

What the four tools are, what the server tells a client the moment it connects, and
what step 3 of the setup above actually produces.

### What the server offers

| Tool | What it does |
| --- | --- |
| `list_documents` | Lists every standard with its id, title, category, description, and tags. No arguments, no document text — call it first to find the id you need. |
| `get_document` | Returns one standard's full markdown, given its catalog id. Ids are exact and case-sensitive. |
| `find_documents_by_tag` | Finds the standards carrying one subject tag, returning each match's id, title, category, and description. No document text. |
| `recommend_skills` | Recommends which standards to turn into durable agent skills for the repository you are working in, and explains how to write them. No arguments, no document text. |

`find_documents_by_tag` matches whole tags first and only falls back to approximate matching
— documents whose tag *contains* what you asked for — when no standard carries the tag
exactly, so asking for `testing` still finds a `unit-testing` standard. The response says
which of the two happened. Its payload deliberately omits `tags`, so use `list_documents` to
see the tag vocabulary the catalog actually uses.

### What the server says on connect

MCP lets a server return an `instructions` string during the initialization handshake, and
clients typically put it in the model's system prompt. This server uses it, so an agent
knows what the standards are without calling anything — and so `recommend_skills` actually
gets called, which a tool description alone never achieves: a description is read once a
model is already looking for a tool, and "set this repository up to follow the standards"
is not a task anyone asks for.

The text is short, fixed, and identical for every client. It says what the server serves,
how the tools relate, and one directive: **on first use in a workspace with no skills from
this server, call `recommend_skills`, judge the candidates against the repository, and
write the ones that apply** — into your client's conventional skills location and nowhere
else, after saying what you are generating and where.

Three things worth being explicit about:

- **The agent does the writing, not the server.** The server returns text. It has no access
  to your filesystem and cannot create, stage, or track a file. Every file that appears was
  written by your agent with your agent's tools, under your client's own permission model.
- **It is a one-time bootstrap, not a per-session action.** Instructions are re-sent on
  every connect, so the directive is conditional: an agent that finds these skills already
  present leaves them alone. The back-reference in each generated skill is what makes that
  check possible.
- **Ignoring it is a supported outcome.** Instructions are guidance, not protocol. An agent
  that never acts on the directive gets a server that works exactly like any other — the
  four tools, on request. There is no setting to suppress the text because there is nothing
  to suppress: not following it costs you nothing.

Placement guidance names the conventional locations for a few widely used clients —
`.claude/skills/<name>/SKILL.md`, `.github/instructions/<name>.instructions.md`,
`.cursor/rules/<name>.mdc` — and tells anything else, or any client whose convention has
moved on, to use its own. Those conventions do move, so the fallback is the part that
matters; the list is a convenience, never exhaustive.

### Turning the standards into skills

The other three tools answer questions when someone remembers to ask. `recommend_skills`
exists for the other case: making a repository follow the standards by default, by turning
the relevant ones into agent skills that are always loaded.

Call it once when setting a repository up. It returns every current standard as a
*candidate* — `id`, `title`, `description`, `category`, `status`, `tags` — plus the
procedure for writing skills from them:

1. **Look at the repository first.** Which languages and frameworks, how it is laid out,
   what it actually has — a frontend, an API, messaging, a database, tests.
2. **Skip the candidates that do not apply.** A skill for a standard the codebase cannot
   exercise is not free: it competes for attention with the ones that do apply. This step
   is the agent's, not the server's — only the agent can see the codebase, so the tool
   returns *everything* eligible rather than guessing.
3. **Fetch only what survived.** One `get_document` call per kept candidate. The
   recommendation carries no document text, so the token cost lands only on the standards
   that made the cut.
4. **Write one skill per kept document**, each carrying an identifier from the document, a
   description saying *when* it applies, content distilled from the document's guidance,
   and a back-reference to this server, `get_document`, and the document's id.

That back-reference is the part worth keeping. It makes the skill an index into the
authoritative document rather than a copy of it: cheap to keep loaded, and one call away
from the full standard when the question gets specific.

Candidates exclude anything `superseded` or `deprecated` — a retired standard would teach a
retracted rule. `draft` standards are included, carrying their status, and the instructions
tell the agent to treat them as provisional.

Two things the server deliberately does not do. It **writes and tracks nothing** — no file,
no record of what it recommended, no memory of any skill anyone generated; the tool is a
read of the catalog plus fixed instruction text, and two calls over the same catalog return
the same bytes. And it **prescribes no format** — what a skill file looks like and where it
lives are your client's conventions, which your client knows and this server does not.

The consequence of tracking nothing is that **a generated skill is not invalidated when the
standard changes.** Nothing notifies you, and nothing here can. The back-reference is the
mitigation, not a fix: the skill points at the live document, so an agent that follows it
gets current content. If a standard changes materially, re-run `recommend_skills` and
rewrite the affected skills.

**Freshness:** nothing the server returns is more than **30 minutes old**. The catalog and
each document body are cached separately for 30 minutes, so a standard merged to `main`
appears within half an hour without a deployment.

## Adding or changing a standard

1. Write the document in the folder matching its kind, following that folder's `0000-*`
   template:

   | Folder | For |
   | --- | --- |
   | [`docs/ADR/`](docs/ADR) | Architecture decision records — a decision, its context, its alternatives, its consequences |
   | [`docs/Designs/`](docs/Designs) | Coding designs, patterns, and conventions to follow |
   | [`docs/Structures/`](docs/Structures) | File, folder, and project structure standards |

2. Open the document with a single level-one heading. That heading is the document's title,
   and it must match its catalog entry exactly.

3. Add the document to [`docs/index.json`](docs/index.json), the authoritative catalog. Every
   entry carries `id`, `title`, `description`, `category`, `status`, `tags`, and `path`.

   The server does not crawl folders: **a document missing from the catalog is invisible**,
   and an entry pointing at a missing file fails at runtime. CI fails the build on either.
   If you use Claude Code, the `docs-index` skill maintains the catalog for you.

4. Check it before pushing:

   ```powershell
   dotnet run --project tools/HexMaster.CodingStandards.CatalogValidator -- .
   ```

Merging to `main` is enough — the running server picks the change up within 30 minutes.
No deployment, no image build.

## Working on the server

```powershell
dotnet restore
dotnet build                                            # warnings are errors in CI
dotnet test                                             # xUnit v3, no network required
dotnet run --project src/HexMaster.CodingStandards.Mcp   # then GET /health

dotnet publish src/HexMaster.CodingStandards.Mcp -c Release -t:PublishContainer   # build + push nvv54gsk4pteu.azurecr.io/servers/mcp/coding-standard:<gitversion>
az bicep build --file infra/main.bicep
```

| Path | What it is |
| --- | --- |
| `src/HexMaster.CodingStandards.Mcp` | The protocol edge: MCP over HTTP, DI composition, `Tools/`, `/health` |
| `src/HexMaster.CodingStandards.Docs` | Everything about documents: GitHub download, cache, retrieval, index, keyword search |
| `tests/HexMaster.CodingStandards.Docs.Tests` | xUnit v3, offline, fixture-driven — everything about documents, tested with no host |
| `tests/HexMaster.CodingStandards.Mcp.Tests` | The tool responses, and the two instruction texts — the connect-time one and `recommend_skills`' |
| `tools/HexMaster.CodingStandards.CatalogValidator` | `validate-catalog`, run by CI and locally |
| `infra/` | Bicep: Container Apps environment, managed certificate, container app (the registry already exists) |
| `openspec/` | Change proposals and capability specs |

The dependency runs one way — `Mcp` → `Docs` — which is what keeps the document logic
testable without a web host. See [`CLAUDE.md`](CLAUDE.md) for the architecture decisions
worth knowing before changing any of it.

## Licence

[MIT](LICENSE).
