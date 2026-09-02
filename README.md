# HexMaster Coding Standards

HexMaster's coding standards and guidelines, served to AI coding agents over the
[Model Context Protocol](https://modelcontextprotocol.io/).

The standards themselves are the markdown in [`docs/`](docs) — architecture decision records,
coding designs, and project structure standards. The MCP server downloads them from this
repository at runtime, so **publishing a standard is a merge to `main`**, not a deployment.

## Connecting an MCP client

The server runs as an Azure Container App and scales to zero when idle, so the first request
after a quiet period takes a few seconds.

```json
{
  "servers": {
    "hexmaster-coding-standards": {
      "type": "http",
      "url": "https://<endpoint>"
    }
  }
}
```

The endpoint is published in the deployment summary of the most recent
[CD run](../../actions/workflows/cd.yml).

### What the server offers

| Tool | What it does |
| --- | --- |
| `list_documents` | Lists every standard with its id, title, category, description, and tags. No arguments, no document text — call it first to find the id you need. |
| `get_document` | Returns one standard's full markdown, given its catalog id. Ids are exact and case-sensitive. |
| `find_documents_by_tag` | Finds the standards carrying one subject tag, returning each match's id, title, category, and description. No document text. |

`find_documents_by_tag` matches whole tags first and only falls back to approximate matching
— documents whose tag *contains* what you asked for — when no standard carries the tag
exactly, so asking for `testing` still finds a `unit-testing` standard. The response says
which of the two happened. Its payload deliberately omits `tags`, so use `list_documents` to
see the tag vocabulary the catalog actually uses.

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

docker build -t hexmaster-coding-standards .
az bicep build --file infra/main.bicep
```

| Path | What it is |
| --- | --- |
| `src/HexMaster.CodingStandards.Mcp` | The protocol edge: MCP over HTTP, DI composition, `Tools/`, `/health` |
| `src/HexMaster.CodingStandards.Docs` | Everything about documents: GitHub download, cache, retrieval, index, keyword search |
| `tests/HexMaster.CodingStandards.Docs.Tests` | xUnit v3, offline, fixture-driven |
| `tools/HexMaster.CodingStandards.CatalogValidator` | `validate-catalog`, run by CI and locally |
| `infra/` | Bicep: Container Apps environment, container app (the registry already exists) |
| `openspec/` | Change proposals and capability specs |

The dependency runs one way — `Mcp` → `Docs` — which is what keeps the document logic
testable without a web host. See [`CLAUDE.md`](CLAUDE.md) for the architecture decisions
worth knowing before changing any of it.

## Licence

[MIT](LICENSE).
