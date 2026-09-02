# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A hosted MCP server that serves HexMaster's coding standards and guidelines to MCP clients. The standards themselves are markdown documents in this repository under `/docs`; the server **downloads them from GitHub at runtime** rather than shipping them in its image, so publishing a standard is a merge to `main`, not a deployment.

The repository is public: `https://github.com/hexmasternl/hexmaster-coding-standards`.

## Project structure

```
HexMaster Coding Standards.slnx                # solution, repository root
global.json                                    # pinned SDK + MTP test-runner opt-in
Directory.Build.props                          # TFM, nullable, warnings-as-errors for all projects
Dockerfile                                     # multi-stage, linux-x64, chiselled ASP.NET base

src/HexMaster.CodingStandards.Mcp/             # ASP.NET Core host
  Program.cs                                   #   composition: MCP HTTP transport, DI, health
  Tools/                                       #   MCP tool classes (one class per tool)

src/HexMaster.CodingStandards.Docs/            # the only component that touches documents
                                               #   downloads the repo archive from GitHub,
                                               #   caches it in memory, refreshes on a TTL,
                                               #   and serves retrieval / index / keyword search

tests/HexMaster.CodingStandards.Docs.Tests/    # xUnit v3, offline, fixture-driven

tools/HexMaster.CodingStandards.CatalogValidator/  # `validate-catalog`, run by CI and locally

docs/                                          # the served content
  index.json                                   #   authoritative catalog of every document
  ADR/                                         #   architecture decision records
  Designs/                                     #   coding designs, patterns, conventions
  Structures/                                  #   file / folder / project structure standards

infra/                                         # Bicep, resource-group scoped
  main.bicep                                   #   entry point, composes modules
  modules/                                     #   registry, environment, containerApp
  params/prod.bicepparam                       #   prod, swedencentral

.github/workflows/                             # ci.yml (PRs + main), cd.yml (main + manual)
```

### The two projects

- **`Mcp`** — the protocol edge. Serves content over the MCP protocol using `ModelContextProtocol.AspNetCore` over HTTP transport in **stateless mode**. Owns transport, hosting, DI composition, the `Tools/` folder, and `GET /health`.
- **`Docs`** — the document layer, and the primary connection to the content. It downloads the documents from the public GitHub repository and exposes a service that retrieves a document by id, provides an index of all available documents, and searches documents by keyword.

The dependency runs one way: `Mcp` → `Docs`. The `Docs` project must not reference the host or ASP.NET Core hosting types — that is what keeps the interesting logic testable without a `WebApplicationFactory`.

## Commands

Run from the repository root.

```powershell
dotnet restore
dotnet build                                          # warnings are errors in CI
dotnet test                                           # xUnit v3, no network required
dotnet test --filter "FullyQualifiedName~<TestName>"  # a single test
dotnet run --project src/HexMaster.CodingStandards.Mcp

dotnet run --project tools/HexMaster.CodingStandards.CatalogValidator -- .   # validate docs/index.json

docker build -t hexmaster-coding-standards .
az bicep build --file infra/main.bicep
az bicep build-params --file infra/params/prod.bicepparam
```

`dotnet test` needs the `"test": { "runner": "Microsoft.Testing.Platform" }` block in
`global.json`. xUnit v3 runs on Microsoft.Testing.Platform, and the .NET 10 SDK refuses the
retired VSTest bridge — without that opt-in, `dotnet test` fails before running anything.

The server is reachable on its Kestrel port locally (see `Properties/launchSettings.json`); in the container it listens on **8080** via `ASPNETCORE_HTTP_PORTS`.

## Architecture notes worth knowing before you change things

- **Content comes from GitHub at runtime.** A refresh downloads the repository **archive for the configured ref in a single request** and takes the catalog and every document body from that one archive. This is deliberate: one request per refresh keeps unauthenticated rate limits irrelevant, and it guarantees the catalog and the bodies come from the same commit. Do not replace it with per-document `raw.githubusercontent.com` fetches.
- **The archive comes from the API tarball endpoint** (`api.github.com/repos/{owner}/{repo}/tarball/{ref}`), not a codeload URL, because that one path resolves a branch, a tag, or a commit SHA. Codeload needs the ref kind baked into the URL (`refs/heads/` versus `refs/tags/`), which would make `Documents__Ref` accept branches only. GitHub rejects these requests without a user agent, so the named `HttpClient` sets one.
- **A truncated or non-tarball response becomes `ContentUnavailableException`.** GitHub serving an error page, or a cut-short download, would otherwise surface as a raw `EndOfStreamException`/`InvalidDataException` and bypass the fall-back-to-cached-content path. The translation in `ContentArchiveExtractor` is what keeps that path reachable.
- **The cache is in-memory and per-replica**, refreshed on a TTL (default 15 minutes) and swapped atomically. A failed refresh keeps serving the last good content; only a replica that has *never* loaded content is unhealthy. `GET /health` encodes exactly that distinction — keep it that way, because it is what stops a GitHub outage from becoming an outage here.
- **`index.json` is authoritative.** The server does not crawl folders. A document missing from the catalog is invisible; an entry pointing at a missing file is a runtime failure. CI fails the build when the catalog and the tree disagree. When you add, change, or delete anything under `/docs`, the `docs-index` skill updates the catalog in the same change.
- **Archive extraction is confined to the `docs/` prefix.** Entries resolving outside it — absolute paths, traversal segments, links — are skipped and logged. The archive is untrusted input; do not loosen this.
- **Stateless MCP mode is load-bearing.** The container app runs `minReplicas: 0` with HTTP scaling, so consecutive requests from one client can land on different replicas. No per-session state, no sampling, no elicitation.
- **No in-process HTTPS redirection.** Container Apps ingress terminates TLS and forwards plain HTTP; `UseHttpsRedirection()` would cause a redirect loop. HTTPS is enforced at the edge (`allowInsecure: false`).
- **Search is a deliberate in-memory scan** over title, description, tags, and body, ranking metadata matches above body matches. At tens of documents an index would be infrastructure serving a problem this project does not have. It sits behind an interface if that changes.
- **The container image is framework-dependent `linux-x64`.** The template's `SelfContained` / `PublishSingleFile` / multi-RID settings were removed on purpose — the base image supplies the runtime, and cold-start time matters under scale-to-zero.
- **CD deploys Bicep twice per release**: deploy infra → build and push the image to the now-existing registry → redeploy infra pinned to the image SHA. That is what makes a fresh subscription self-bootstrapping. Pushes touching only `docs/**` do **not** trigger CD.
- **No secrets anywhere.** Azure auth is OIDC federated credentials (`vars.*` only, no `secrets.*` in either workflow); the container app pulls from the registry with a user-assigned managed identity holding `AcrPull`, and registry admin credentials are disabled.
- **Two Bicep details that look odd and are not.** The `AcrPull` role assignment lives *inside* `modules/registry.bicep` because an assignment name must be computable at the start of the deployment, and the registry's resource id only is inside that module. And `main.bicep` omits the app's `registries` block entirely while the image is the `mcr.microsoft.com` placeholder — naming a registry that holds no image yet would fail the first revision, which is exactly the bootstrap case the placeholder exists for.

## Conventions

- Place projects under `src/`, tests under `tests/`, and keep the solution file at the repository root.
- Images are tagged with the full commit SHA (plus a moving `latest`); infrastructure always references the SHA tag, never `latest`, so a rollback is a redeploy rather than a rebuild.
- Specs and change proposals live in `openspec/`. See `openspec/changes/` for in-flight work and `openspec/specs/` for the current capability specs.
