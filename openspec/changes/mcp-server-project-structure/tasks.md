## 1. Solution restructure

- [x] 1.1 Add `global.json` at the repository root pinning the .NET 10 SDK version
- [x] 1.2 Move `HexMaster Coding Standards.slnx` from `src/` to the repository root and fix the project path
- [x] 1.3 Rewrite `src/HexMaster.CodingStandards.Mcp/HexMaster.CodingStandards.Mcp.csproj`: remove `SelfContained`, `PublishSelfContained`, `PublishSingleFile`, and the multi-platform `RuntimeIdentifiers` list; keep `net10.0`, nullable, implicit usings, and the `ModelContextProtocol.AspNetCore` reference
- [x] 1.4 Create `src/HexMaster.CodingStandards.Docs/HexMaster.CodingStandards.Docs.csproj` as a `net10.0` class library, referencing no ASP.NET Core hosting packages beyond `Microsoft.Extensions.*` abstractions
- [x] 1.5 Create `tests/HexMaster.CodingStandards.Docs.Tests/` as an xUnit v3 test project referencing the Docs project only
- [x] 1.6 Add the Docs and test projects to the solution, and add the Mcp → Docs project reference
- [x] 1.7 Delete `src/HexMaster.CodingStandards.Mcp/Tools/RandomNumberTools.cs`, leaving the `Tools/` folder in place with a placeholder so it survives in git
- [x] 1.8 Verify `dotnet build` and `dotnet test` succeed from the repository root with no warnings

## 2. Document content tree

- [x] 2.1 Create `docs/ADR/`, `docs/Designs/`, and `docs/Structures/`
- [x] 2.2 Write a template ADR in `docs/ADR/` demonstrating the expected shape (decision, context, alternatives, consequences), opening with a level-one heading
- [x] 2.3 Write a template design document in `docs/Designs/` with a level-one heading
- [x] 2.4 Write a template structure document in `docs/Structures/` with a level-one heading
- [x] 2.5 Create `docs/index.json` with a `documents` array holding one entry per template, each with `id`, `title`, `description`, `category`, `status` (`draft`), `tags`, and `path`, sorted by category then id
- [x] 2.6 Confirm each entry's `title` matches its document's level-one heading and each `path` resolves

## 3. Catalog model and validation

- [x] 3.1 Add catalog model types to the Docs project for the catalog root and its entries, with `category` and `status` as constrained value types over `ADR`/`Design`/`Structure` and `draft`/`accepted`/`superseded`/`deprecated`
- [x] 3.2 Implement catalog parsing that skips and logs individually invalid entries (missing required property, unknown category or status, duplicate id) while returning the valid ones, and fails outright on unparseable JSON
- [x] 3.3 Add a validation routine that cross-checks a catalog against a document set: duplicate ids, unresolved paths, category/folder mismatch, unindexed documents, and title/heading drift
- [x] 3.4 Add unit tests for parsing and validation covering each failure mode and the all-consistent case
- [x] 3.5 Add a repository-root validation entry point CI can invoke against `docs/` and have it exit non-zero on any inconsistency
- [x] 3.6 Verify the validator passes against the real `docs/` tree, and fails when a document is deliberately unindexed

## 4. GitHub content acquisition

- [x] 4.1 Add an options type for repository owner, repository name, ref, refresh interval, and optional access token, defaulting to `hexmasternl/hexmaster-coding-standards`, `main`, and 15 minutes
- [x] 4.2 Define the interface that fetches raw content for a ref, so the download is substitutable in tests
- [x] 4.3 Implement the archive downloader: one authenticated-or-anonymous HTTPS request for the repository archive of the configured ref, using a named `HttpClient` with a timeout and retry
- [x] 4.4 Implement archive extraction confined to the `docs/` prefix, skipping and logging entries whose resolved path escapes it (absolute paths, traversal segments, links) and ignoring entries outside `docs/`
- [x] 4.5 Add unit tests over fixture archives covering a normal archive, a traversing entry, an absolute-path entry, non-`docs` entries, and an archive missing `index.json`
- [x] 4.6 Add a test proving a catalogued path absent from the archive is logged at load time and yields not-found on retrieval

## 5. Document service

- [x] 5.1 Define the document service interface: retrieve by id, list the index, search by keyword — with a result shape that distinguishes not-found and not-ready from success
- [x] 5.2 Implement the in-memory content set holding the catalog plus every document body, populated from an extracted archive
- [x] 5.3 Implement retrieval by exact, case-sensitive id, returning metadata plus full markdown body, and not-found without fuzzy fallback
- [x] 5.4 Implement the index listing: one entry per catalogued document with all six metadata fields and no bodies
- [x] 5.5 Implement keyword search over title, description, tags, and body — case-insensitive, ranking title and tag matches above body-only matches, rejecting blank keywords, and returning index entries
- [x] 5.6 Implement the cache holder with atomic replacement, so no reader observes a partially loaded set
- [x] 5.7 Implement the background refresh service on the configured interval: keep the last good set when a refresh fails, and report not-ready when no set has ever loaded
- [x] 5.8 Add a single DI registration extension method wiring the service, HTTP client, options, and background refresh
- [x] 5.9 Add unit tests for retrieval, index listing, search ranking and case-insensitivity, blank-keyword rejection, empty-versus-failure distinction, atomic swap, and the failed-refresh-after-success versus failed-cold-start behaviours

## 6. MCP host wiring

- [x] 6.1 Rewrite `Program.cs`: MCP server with HTTP transport in stateless mode, `MapMcp()`, and the Docs registration call; remove `app.UseHttpsRedirection()`
- [x] 6.2 Bind the Docs options from configuration so every setting is environment-variable overridable, and keep the access token out of any startup logging
- [x] 6.3 Add a health check backed by the document service's readiness and map `GET /health` unauthenticated
- [x] 6.4 Set the container HTTP port to 8080 via `ASPNETCORE_HTTP_PORTS` and update `Properties/launchSettings.json` for local runs
- [x] 6.5 Rewrite the host project's `README.md`, replacing the template's random-number and self-contained-publish content with this server's actual local-run and client-configuration instructions
- [x] 6.6 Run the server locally and verify an MCP client can initialize, that `/health` returns 200 once content loads, and that no HTTPS redirect is issued on plain HTTP

## 7. Container image

- [x] 7.1 Add a multi-stage `Dockerfile` at the repository root: SDK stage restoring and publishing the host project framework-dependent for `linux-x64`, runtime stage on the chiselled ASP.NET base image
- [x] 7.2 Run as a non-root user, expose 8080, and copy no `docs/` content into the image
- [x] 7.3 Add a `.dockerignore` excluding `bin`, `obj`, `docs`, `infra`, `.git`, and `openspec`
- [x] 7.4 Build the image and verify `/health` returns 200 with outbound network access, and that the image contains no `index.json`

## 8. Bicep infrastructure

- [x] 8.1 Create `infra/modules/registry.bicep`: Azure Container Registry with the admin user disabled
- [x] 8.2 Create `infra/modules/environment.bicep`: Log Analytics workspace plus a Container Apps managed environment wired to it
- [x] 8.3 Create `infra/modules/containerApp.bicep`: container app with external ingress on 8080, `allowInsecure: false`, `minReplicas: 0`, parameterised `maxReplicas`, an HTTP concurrency scale rule, consumption workload profile, and the content-source settings as environment variables
- [x] 8.4 Add a user-assigned managed identity and grant it `AcrPull` on the registry, and configure the container app to pull using that identity with no registry credentials
- [x] 8.5 Create `infra/main.bicep` at resource-group scope composing the modules, taking `containerImage` as a parameter defaulting to a publicly pullable placeholder image, tagging every resource with application and environment, and outputting the container app FQDN
- [x] 8.6 Create `infra/params/prod.bicepparam` targeting `swedencentral` with the agreed resource naming
- [ ] 8.7 Verify `az bicep build` is clean and a what-if deployment against the resource group reports changes without error

## 9. GitHub Actions workflows

- [x] 9.1 Add `.github/workflows/ci.yml` on pull requests targeting `main` and pushes to `main`: restore, build with warnings as errors, `dotnet test`, catalog validation, and `az bicep build` — with no Azure credentials required
- [x] 9.2 Add `.github/workflows/cd.yml` on pushes to `main` touching `src/**`, `infra/**`, or the workflow file, plus `workflow_dispatch`, with `id-token: write` and OIDC login, and no `docs/**` trigger
- [x] 9.3 Implement the CD job order: deploy `main.bicep`, then build and push the image tagged with the full commit SHA and `latest`, then redeploy `main.bicep` pinned to the SHA tag
- [x] 9.4 Source subscription, resource group, and registry from GitHub Environment configuration rather than workflow literals, and confirm no secret, password, or connection string appears in either workflow
- [x] 9.5 Add a post-deployment health check that fails the run if the deployed app does not report healthy, and write the app's HTTPS endpoint to the run summary
- [ ] 9.6 Trigger CD manually against the empty resource group and verify the bootstrap path: registry created, image pushed, app pinned to the SHA, endpoint healthy

## 10. Documentation and close-out

- [x] 10.1 Expand the root `README.md` with what the server is, its public endpoint, how to add a document under `/docs`, and how to point an MCP client at it
- [x] 10.2 Reconcile `CLAUDE.md` against what was actually built, correcting any command, path, or behaviour that drifted during implementation
- [x] 10.3 Add the ADR recording the runtime-download-versus-baked-image decision as the first real document under `docs/ADR/`, indexed via the `docs-index` skill
- [ ] 10.4 Confirm a full green run: `dotnet build`, `dotnet test`, catalog validation, `docker build`, `az bicep build`, and a successful CD run
