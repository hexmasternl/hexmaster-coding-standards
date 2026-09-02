# HexMaster Coding Standards

HexMaster's coding standards and guidelines, served to AI coding agents over the
[Model Context Protocol](https://modelcontextprotocol.io/).

The standards themselves are the markdown in [`docs/`](docs) — architecture decision records,
coding designs, and project structure standards. The MCP server downloads them from this
repository at runtime, so **publishing a standard is a merge to `main`**, not a deployment.

## Connecting an MCP client

The server lives at **`https://standards-mcp.hexmaster.nl`** — one remote HTTP MCP endpoint,
no authentication, nothing to install. The URL is the entire configuration.

It runs as an Azure Container App with `minReplicas: 0`, so it scales to zero when idle and
the first request after a quiet period takes a few seconds. A client that gives up instantly
on a cold start usually succeeds on the next try.

Two config shapes exist in the wild — `mcpServers` (Claude, Cursor) and `servers` (VS Code,
Visual Studio) — but the object inside is the same. Below is each client's.

### Claude Code

From the repository you want the standards in:

```powershell
claude mcp add --transport http --scope project hexmaster-coding-standards https://standards-mcp.hexmaster.nl
```

`--scope project` writes `.mcp.json` in the repository root. That is the scope to want for a
team standard: the file is checked in, so everyone who clones the repository gets the server.
Use `--scope user` instead to have it in every project on your machine.

The same thing written by hand — `.mcp.json` at the repository root:

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

Run `/mcp` in a session to check: the server should be listed as connected, with four tools.

### Claude Desktop and claude.ai

Settings → **Connectors** → **Add custom connector**, then paste the URL. A remote MCP server
needs no local proxy and no command — name it, give it the URL, save. It then appears in the
attachments/tools menu of a conversation, where it can be switched on per chat.

### GitHub Copilot

**VS Code** — put this in `.vscode/mcp.json` to share it with the repository, or add it to
your user profile via the **MCP: Add Server…** command (choose *HTTP*, paste the URL) to have
it everywhere:

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

Copilot Chat only calls MCP tools in **Agent** mode; in Ask mode the server is connected and
never used. The tools picker in the chat input is where you confirm the four tools arrived.

**Visual Studio** — the same JSON, in `.mcp.json` next to the `.sln`/`.slnx` (see
[MCP servers in Visual Studio](https://learn.microsoft.com/visualstudio/ide/mcp-servers)).

**The Copilot coding agent** on github.com is configured separately, in the repository's
Settings → Copilot → Coding agent → MCP configuration, where entries take an explicit tool
allow-list:

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

`.cursor/mcp.json` in the repository, or `~/.cursor/mcp.json` for every project:

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

Cursor Settings → **Tools & MCP** lists the server once the file is saved; make sure it is
enabled and showing its tools. Agent mode is what calls them.

### Anything else

Any client that speaks MCP over streamable HTTP can connect to the same URL — there is no
stdio command to run, because there is nothing to install locally. If a client only supports
stdio, put a generic remote-MCP proxy in front of it rather than asking for a variant here.

The container app's own `*.azurecontainerapps.io` ingress hostname answers identically and is
published in the deployment summary of the most recent
[CD run](../../actions/workflows/cd.yml) — useful if the custom domain is ever in doubt.

`GET https://standards-mcp.hexmaster.nl/health` returns `Healthy` once a replica has the
catalog loaded, which is the quickest way to tell a cold start from a real problem before
blaming your client's configuration.

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
