# Design: MCP server project structure

## Context

The repository is a near-empty scaffold: one commit, an MIT `LICENSE`, a one-line `README.md`, `VisualStudio.gitignore`, and `src/` containing the output of the `dotnet new mcpserver` template — a `net10.0` ASP.NET Core app referencing `ModelContextProtocol.AspNetCore` 1.2.0, configured for self-contained single-file publish across six runtime identifiers, exposing a sample `RandomNumberTools`.

The target system is a publicly hosted MCP server that serves HexMaster's coding standards. Its content is markdown kept in this same public repository under `/docs`, split into `ADR`, `Designs`, and `Structures`, catalogued by `/docs/index.json`. It runs as an Azure Container App that scales to zero when idle, provisioned with Bicep and deployed by GitHub Actions.

Constraints that shape this design:

- The template's publish settings (self-contained, single-file, six RIDs) are aimed at desktop/stdio distribution and are wrong for a container image.
- Scale-to-zero means cold starts, and it means any replica may serve any request, so the server must hold no per-session state.
- The content lives in the same repository as the code, so content and code share a commit and a CI pipeline.
- Azure Container Apps needs a container registry that exists *before* the first image push, while the registry itself is provisioned by the Bicep that CD runs — an ordering problem that must be solved without a manual bootstrap step.
- Nothing exists yet, so there is no migration burden and no backwards compatibility to keep.

## Goals / Non-Goals

**Goals:**

- A repository layout that makes the four concerns — content, application code, infrastructure, pipeline — visibly separate and independently reviewable.
- A solution that restores, builds, tests, and runs locally with the standard `dotnet` commands, with no Azure dependency.
- A content model where `index.json` is unambiguous enough that later changes can build catalog and retrieval tools against it without renegotiating the shape.
- Infrastructure and pipeline skeletons that actually deploy the placeholder server end-to-end, so the first functional change inherits a working path to production rather than an untested one.
- Domain logic (document loading, catalog parsing) reachable from unit tests without spinning up the web host.

**Non-Goals:**

- Any MCP tool that serves document content, searches, or filters. The tools folder is created and wired, but empty.
- Authoring real ADRs, designs, or structure documents beyond one template per category.
- Authentication, authorization, rate limiting, or a custom domain on the MCP endpoint.
- Caching strategy, content hot-reload, or observability beyond default Container Apps log streaming to Log Analytics.
- Multi-region or high-availability topology.

## Decisions

### Content is baked into the container image

The `/docs` tree is copied into the image at build time and read from the filesystem at runtime.

*Why:* it makes a deployment immutable and reproducible — an image tag pins both code and content. It removes GitHub as a runtime dependency, avoids unauthenticated `raw.githubusercontent.com` rate limits, and keeps the cold-start path free of network fetches, which matters when the app scales from zero on the first request.

*Alternative considered:* fetching documents from the GitHub API or raw URLs at runtime with an in-memory cache. That decouples content updates from deploys — publish a doc without a redeploy — but adds a network failure mode on the cold-start path, needs cache invalidation, and makes "what did the server return" unanswerable from an image tag. Rejected for now; the CD workflow triggers on `docs/**` changes too, so a content edit still reaches production automatically on merge. Revisit if content edit frequency makes redeploys annoying.

### `index.json` is the authoritative catalog, validated in CI

Every served document has exactly one entry in `/docs/index.json` carrying `id`, `title`, `description`, `category`, `status`, `tags`, and a repository-relative `path`. The server reads the catalog from this file; it does not crawl the folders or parse front matter to discover documents.

*Why:* a single hand-maintained file keeps document metadata reviewable in a pull request diff and keeps the server's read path trivial. Discovery-by-crawl would make the served surface an emergent property of the filesystem, which is harder to review and easier to break by accident.

*Trade-off:* the catalog can drift from the tree — an entry pointing at a deleted file, a document nobody indexed, a duplicate `id`. This is mitigated by a CI validation step that fails the build on drift, which is why validation lives in the `docs-content-structure` spec rather than being left to convention.

*Alternative considered:* generating `index.json` from YAML front matter in each document. It cannot drift, but it puts a generated file in source control (or a generation step in the runtime), and metadata review moves from one diff to many.

### Two projects plus a test project, solution at the repository root

```
HexMaster Coding Standards.slnx        # repo root, per CLAUDE.md
src/HexMaster.CodingStandards.Core/    # class library: catalog + document domain
src/HexMaster.CodingStandards.Mcp/     # ASP.NET Core host: MCP transport, tools, DI
tests/HexMaster.CodingStandards.Core.Tests/
```

*Why:* the interesting logic — parsing the catalog, resolving a document path safely, filtering by category or tag — is pure and belongs somewhere a unit test can reach without a `WebApplicationFactory`. The host project keeps transport, hosting, and tool attributes. The split is the smallest one that buys testability; no separate `Domain`/`Application`/`Infrastructure` layering, which this project does not yet earn.

The solution file moves from `src/` to the repository root, as `CLAUDE.md` already prescribes.

### Container image instead of self-contained single-file publish

The template's `SelfContained`, `PublishSelfContained`, `PublishSingleFile`, and six-RID `RuntimeIdentifiers` are removed. The app becomes a framework-dependent `linux-x64` build in a multi-stage `Dockerfile` on the chiselled ASP.NET runtime base image, listening on port 8080 via `ASPNETCORE_HTTP_PORTS`.

*Why:* the runtime is supplied by the base image, so self-contained publish only inflates the image. Single-file publish additionally complicates reading `/docs` from disk and offers nothing in a container. Chiselled base images are small and run as non-root, which suits a public endpoint.

*Alternative considered:* keeping the multi-RID self-contained publish so the same artifact could also be distributed as a stdio MCP server. Rejected — hosting is the stated goal, and the settings can come back in the project file if local distribution is ever wanted.

### `UseHttpsRedirection` is removed, and the MCP transport stays stateless

Container Apps ingress terminates TLS and forwards plain HTTP to the container. Leaving `app.UseHttpsRedirection()` in place makes the app see an HTTP request and issue a redirect, producing a loop or a broken client. It is removed; `external: true` ingress with `allowInsecure: false` enforces HTTPS at the edge.

The template's `options.Stateless = true` is kept deliberately, not incidentally. With `minReplicas: 0` and HTTP-based scaling, consecutive requests from one client can land on different replicas, and a replica can disappear between them. Stateless mode means no server-to-client requests (sampling, elicitation) and no session-affinity requirement — which is exactly the constraint scale-to-zero imposes.

### Bicep: one resource-group-scoped `main.bicep` with modules, deployed twice per release

```
infra/main.bicep                   # targetScope = resourceGroup
infra/modules/registry.bicep
infra/modules/environment.bicep    # Log Analytics + Container Apps environment
infra/modules/containerApp.bicep
infra/params/prod.bicepparam
```

A single `prod` environment ships now, in `swedencentral`. The parameter file is still a separate artifact from `main.bicep` rather than inlined defaults, so adding a `dev.bicepparam` later is an additive change with no template edit.

`main.bicep` takes a `containerImage` parameter that defaults to a public placeholder image. CD runs: **deploy infra**, then **build and push the image to the now-existing registry**, then **deploy infra again with the real image reference**.

*Why:* this makes the stack self-bootstrapping. On a fresh subscription the first deployment creates the registry and an app running the placeholder; the second deployment in the same run swaps in the real image. No manual "run this once" step, and every subsequent run is two idempotent deployments — cheap, because ARM no-ops unchanged resources.

*Alternative considered:* splitting a long-lived `platform.bicep` (registry, environment, workspace, identity) from a per-release `app.bicep`, with the platform deployed on `workflow_dispatch`. Fewer deployments per release and a cleaner conceptual split, but it introduces a bootstrap step a newcomer will forget and two stacks that can disagree. Rejected for a project this size; worth revisiting if platform resources grow.

The container app pulls from the registry using a **user-assigned managed identity** holding `AcrPull`, not registry admin credentials, so no registry password is ever stored in GitHub or in app configuration.

### Scale-to-zero configuration

`minReplicas: 0`, `maxReplicas` parameterised (default 3), with an HTTP scale rule on concurrent requests, on the consumption workload profile.

*Why:* the stated requirement, and the cost model that makes a public side project viable. The accepted cost is a cold start of a few seconds on the first request after an idle period. No external uptime pinger will be added — that would defeat the point.

### GitHub Actions: two workflows, OIDC authentication

`ci.yml` on pull requests and pushes to `main`: restore, build with warnings as errors, test, validate `index.json`, and `bicep build` the templates so infrastructure syntax errors surface in review. `cd.yml` on pushes to `main` touching `src/**`, `docs/**`, `infra/**`, or the workflow itself, plus `workflow_dispatch`.

Azure authentication uses **OIDC federated credentials** on an app registration — no client secret in GitHub. Subscription id, resource group, and environment name come from GitHub Environment variables, so one workflow serves dev and prod with different approval gates.

*Why OIDC:* a long-lived service principal secret in a public repository's settings is a standing risk and needs rotation. Federated credentials are scoped to this repository and branch.

*Image tagging:* the commit SHA, plus a moving `latest`. Tagging by SHA is what lets a rollback be "redeploy the previous tag" rather than a rebuild.

## Risks / Trade-offs

- **Cold start on the first request after idle** → Accepted; it is the point of scale-to-zero. Kept as small as possible by a chiselled base image, framework-dependent publish, and no network I/O during startup. If it becomes unacceptable, the lever is a `minReplicas: 1` flip in a parameter file, not a redesign.
- **Content changes need a redeploy** → CD triggers on `docs/**`, so merging a document deploys it. If the delay or build cost grates, the runtime-fetch alternative above is a contained change behind the Core library's catalog abstraction.
- **`index.json` drifting from the `/docs` tree** → CI validation fails the build on duplicate ids, missing files, and unindexed documents. Without this the server would fail at runtime or silently hide documents in production.
- **Two Bicep deployments per release** → Accepted for self-bootstrapping. Both are idempotent; the cost is seconds, not minutes.
- **A public MCP endpoint with no auth** → Acceptable for the content served (it is already a public repository), but it leaves the endpoint open to abuse. `maxReplicas` bounds the blast radius on cost. Rate limiting and a Front Door / WAF are a deliberate later decision, not an oversight.
- **Path traversal via catalog `path` values** → Document resolution must confine reads to the deployed content root. Called out in the `mcp-server-host` spec so it is designed in rather than patched later, even though no tool serves content yet.
- **Restructuring the template project churns nearly every existing file** → Cheap now, at one commit with no history to preserve; expensive if deferred.
- **`net10.0` plus newer tooling formats (`.slnx`)** → Both are supported on current SDKs, but `.slnx` support in older tooling is thin. Mitigated by pinning the SDK version in `global.json` so CI and local builds agree.

## Migration Plan

Not applicable in the usual sense — there is no running system and no data. The sequencing that matters is within the change: restructure the solution and get `dotnet build` and `dotnet test` green locally, then add content and its validation, then infrastructure, then the pipeline. The pipeline is added last so its first run exercises code that already builds.

Rollback for the deployed app, once the pipeline exists, is redeploying `main.bicep` with the previous image SHA tag.

## Open Questions

- **Resource naming convention** — `hexmaster-codingstandards-prod` for the resource group and a `hexmasterstdprod` style for the registry (which disallows hyphens) will be used unless specified otherwise. Region and environment count are settled: a single `prod` environment in `swedencentral`.
- **The `status` vocabulary for catalog entries** (e.g. `draft` / `accepted` / `superseded` / `deprecated`) — the spec fixes that the field is required and constrained to a known set; the exact set is worth confirming, since ADRs and design documents may want different lifecycles.
- **Whether the resource group is created by the pipeline** (a subscription-scoped deployment) or pre-created out of band. Resource-group scope is assumed, which means the group must exist before the first CD run.
