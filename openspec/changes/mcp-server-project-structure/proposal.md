# Establish MCP server project structure

## Why

The repository currently holds only a template-generated MCP server project (`RandomNumberTools` sample) with no content folder, no infrastructure, and no deployment pipeline. Before any coding-standards functionality can be built, the repository needs an agreed-upon skeleton: where documents live, how they are indexed, how the server is structured, how it is provisioned in Azure, and how it ships. Settling this once, up front, prevents rework across every later change.

## What Changes

- Add a `/docs` content root with `ADR/`, `Designs/`, and `Structures/` subfolders, each seeded with a template document so the intended document shape is unambiguous.
- Add `/docs/index.json` as the single catalog of every served document, with a documented entry schema (`id`, `title`, `description`, `category`, `status`, `tags`) plus a `path` pointing at the document file.
- Restructure the .NET solution into a layered shape: `HexMaster.CodingStandards.Mcp` serves content over the MCP protocol, and `HexMaster.CodingStandards.Docs` owns the connection to the documents — downloading them from the public GitHub repository and exposing a service that retrieves a document, lists the index of all available documents, and searches documents by keyword.
- Add a `tests/` folder with an xUnit v3 unit-test project covering the Docs project.
- Add a `docs-index` agent skill that maintains `/docs/index.json` whenever a document under `/docs` is written, updated, or deleted, deriving each entry's metadata from the document's actual content.
- Replace the sample `RandomNumberTools` with an empty-but-wired tools folder and a health endpoint, keeping the server runnable end-to-end without pretending to serve content yet.
- **BREAKING** (template only): drop the self-contained single-file, multi-RID publish settings in favour of a `linux-x64` container image built from a `Dockerfile`, matching Azure Container Apps hosting.
- Add `/infra` with a Bicep deployment skeleton (Container Apps environment, container app with scale-to-zero, container registry, Log Analytics) and a `prod` parameter file targeting `swedencentral`.
- Add `.github/workflows` with a CI workflow (restore, build, test, validate the catalog) and a CD workflow (build image, push, deploy Bicep) triggered on `main`.
- Document the resulting layout and the real build/test commands in `CLAUDE.md` and `README.md`.

## Capabilities

### New Capabilities
- `docs-content-structure`: The on-disk layout of `/docs`, the three document categories, the document heading conventions, and the `index.json` catalog entry schema and its consistency rules.
- `document-service`: The Docs project's runtime behaviour — downloading the catalog and documents from the public GitHub repository, caching and refreshing them, retrieving a document by id, listing the index, and searching by keyword.
- `mcp-server-host`: The MCP server project layout and composition — solution/project boundaries under `src/` and `tests/`, MCP HTTP transport wiring, tool registration seam, health endpoint, and configuration surface.
- `deployment-infrastructure`: The Bicep module layout and the Azure resources the MCP server is deployed onto, including scale-to-zero behaviour, registry-backed images, and per-environment parameterisation.
- `build-and-release-pipeline`: The GitHub Actions workflow structure — CI on pull requests, CD on `main`, image build/push, and infrastructure deployment — plus the authentication approach.

### Modified Capabilities
<!-- None: openspec/specs/ is empty; this is the first change in the repository. -->

## Impact

- **Code**: `src/HexMaster.CodingStandards.Mcp` (project file rewritten, sample tool removed, `Program.cs` restructured); new `src/HexMaster.CodingStandards.Docs`; new `tests/HexMaster.CodingStandards.Docs.Tests` (xUnit v3); `src/HexMaster Coding Standards.slnx` updated and moved to the repository root per `CLAUDE.md`.
- **Content**: new `/docs` tree and `/docs/index.json`; new `.claude/skills/docs-index/SKILL.md`.
- **Runtime dependency**: the server reads its content from `github.com/hexmasternl/hexmaster-coding-standards` at runtime, so it needs outbound HTTPS and tolerates GitHub being unavailable.
- **Infrastructure**: new `/infra` Bicep files; new Azure resources (Container Apps environment, container app, Azure Container Registry, Log Analytics workspace, managed identity).
- **CI/CD**: new `.github/workflows/*.yml`; requires a federated-credential app registration plus GitHub repository secrets/variables for the Azure subscription and resource group.
- **Dependencies**: `ModelContextProtocol.AspNetCore` stays; the Docs project adds an HTTP client and archive handling; the test project adds xUnit v3 and an assertion library.
- **Docs**: `CLAUDE.md` "Current state" section becomes stale and must be rewritten; `README.md` expanded.
- **Non-goals**: no MCP tools exposing the document service yet, no authoring beyond templates, no custom domain or auth on the MCP endpoint. Those follow in later changes.
