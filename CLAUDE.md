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
                                               #   fetches the catalog and each body from the
                                               #   GitHub Contents API, caches both for 30 min,
                                               #   and serves retrieval / listing / index / search

tests/HexMaster.CodingStandards.Docs.Tests/    # xUnit v3, offline, fixture-driven

tools/HexMaster.CodingStandards.CatalogValidator/  # `validate-catalog`, run by CI and locally

docs/                                          # the served content
  index.json                                   #   authoritative catalog of every document
  ADR/                                         #   architecture decision records
  Designs/                                     #   coding designs, patterns, conventions
  Structures/                                  #   file / folder / project structure standards

infra/                                         # Bicep, resource-group scoped
  main.bicep                                   #   entry point, composes modules
  modules/                                     #   environment, containerApp
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

- **Content comes from GitHub at runtime, one file at a time.** Everything goes through the Contents API (`api.github.com/repos/{owner}/{repo}/contents/{path}?ref={ref}`) with `Accept: application/vnd.github.raw`: the catalog is one request for `docs/index.json`, and a document body is one request made the first time somebody asks for that document. The API rather than `raw.githubusercontent.com`, because it honours the same optional access token as every other call — which is a rate-limited or private-fork deployment's only recourse. GitHub rejects these requests without a user agent, so the named `HttpClient` sets one.
- **Bodies are fetched lazily and are not resident.** Cold start costs one small file, and memory grows only with what clients actually read. The price is request volume: it is no longer one request per refresh but one per document per cache window, so the anonymous limit of 60/hour/IP is now reachable and `Documents__AccessToken` (5,000/hour) is a real lever rather than a formality.
- **Two caches, both in-memory, both per-replica, both 30 minutes by default.** The catalog expires from when it was *loaded*; a body from when that *body* was fetched. Separate clocks, same duration, so the guarantee is one sentence: nothing served is more than 30 minutes old. Both replace atomically, so no reader sees a half-loaded catalog.
- **The catalog loads eagerly at startup and lazily thereafter.** `CatalogLoader` is a `BackgroundService` that loads once and then stops — there is no recurring timer, so an idle replica costs GitHub nothing. **Do not remove the startup load.** Both Container Apps probes read `/health`, `/health` is unhealthy until a catalog has loaded, and a purely lazy loader would never be ready, never be sent a request, and never load: readiness deadlocks and liveness restarts the replica on a loop. Under `minReplicas: 0` every scale-from-zero would hit it.
- **A failed load costs freshness, not availability.** Once a catalog has loaded, a failed reload logs and keeps serving the stale one; only a replica that has *never* loaded is unhealthy, and a failed **body** fetch never affects health at all. `GET /health` encodes exactly that distinction — keep it that way, because it is what stops a GitHub outage from becoming an outage here.
- **Concurrent callers on an expired catalog share one fetch.** `CatalogLoader` holds the in-flight load and hands it to everyone who arrives during it. Without that, an outage turns a burst into one timed-out request per caller. Note the trap in `EnsureCurrentAsync`: a load that completes synchronously runs its own cleanup before the field is assigned, so a completed task must never be stored.
- **`index.json` is authoritative.** The server does not crawl folders. A document missing from the catalog is invisible; an entry pointing at a missing file reports its body as unavailable. CI fails the build when the catalog and the tree disagree. When you add, change, or delete anything under `/docs`, the `docs-index` skill updates the catalog in the same change.
- **Catalog `path` values are untrusted and validated before they become a URL.** `ContentPath` rejects `..` segments, backslashes, absolute paths, anything carrying a scheme or host, and anything outside `docs/` under the three category folders, then percent-encodes each segment. This is the successor to the archive extractor's prefix confinement — the archive is gone, but the untrusted input is not. Do not loosen it.
- **Stateless MCP mode is load-bearing.** The container app runs `minReplicas: 0` with HTTP scaling, so consecutive requests from one client can land on different replicas. No per-session state, no sampling, no elicitation.
- **No in-process HTTPS redirection.** Container Apps ingress terminates TLS and forwards plain HTTP; `UseHttpsRedirection()` would cause a redirect loop. HTTPS is enforced at the edge (`allowInsecure: false`).
- **Search is a deliberate in-memory scan over metadata only** — title, description, and tags, ranking title matches above tag matches above description matches. It does not read bodies, and cannot: bodies are fetched per document and are not resident, so matching them would mean pulling the whole corpus on the first search. At tens of documents an index would be infrastructure serving a problem this project does not have. It sits behind an interface if that changes.
- **The listing payload is five fields on purpose** — `id`, `title`, `category`, `description`, `tags` — and deliberately omits `status`, so a client cannot currently tell an `accepted` standard from a `superseded` or `deprecated` one. `DocumentSummary` still carries it, so adding it back is a one-line change to `DocumentListEntry`. Open question, not an oversight.
- **The container image is framework-dependent `linux-x64`.** The template's `SelfContained` / `PublishSingleFile` / multi-RID settings were removed on purpose — the base image supplies the runtime, and cold-start time matters under scale-to-zero.
- **CD is one push and one deployment.** The registry is *not* part of this repository's infrastructure — it already exists — so a release is: build and push the SHA-tagged image, then deploy Bicep once pinned to that image. There is no bootstrap dance any more, because there is nothing to bootstrap. Pushes touching only `docs/**` do **not** trigger CD.
- **The registry is the one thing reached with secrets.** Azure sign-in is still OIDC federated credentials (`vars.*`, no client secret). The registry is reached with the `ACR_LOGIN_SERVER` / `ACR_LOGIN_USERNAME` / `ACR_LOGIN_PASSWORD` repository secrets: CD logs Docker in with them to push, and passes the same three values to Bicep so the container app can pull. `registryPassword` is a `@secure()` parameter — ARM keeps it out of the deployment history — and lands as the container app secret `registry-password`, which the `registries` block references. There is deliberately no managed identity and no `AcrPull` assignment; the registry's access model is not ours to set.
- **The app's `registries` block is conditional, and the placeholder image is why.** `containerImage` defaults to `mcr.microsoft.com/k8se/quickstart:latest`, and `usesRegistry` (`!empty(registryLoginServer)`) leaves `secrets` and `registries` empty when no credentials were supplied. That is what lets the template be deployed or validated without registry secrets; CD always supplies them.

## Conventions

- Place projects under `src/`, tests under `tests/`, and keep the solution file at the repository root.
- Images are tagged with the full commit SHA (plus a moving `latest`); infrastructure always references the SHA tag, never `latest`, so a rollback is a redeploy rather than a rebuild.
- Specs and change proposals live in `openspec/`. See `openspec/changes/` for in-flight work and `openspec/specs/` for the current capability specs.
