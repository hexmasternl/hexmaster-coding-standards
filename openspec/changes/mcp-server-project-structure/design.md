# Design: MCP server project structure

## Context

The repository is a near-empty scaffold: one commit, an MIT `LICENSE`, a one-line `README.md`, `VisualStudio.gitignore`, and `src/` containing the output of the `dotnet new mcpserver` template — a `net10.0` ASP.NET Core app referencing `ModelContextProtocol.AspNetCore` 1.2.0, configured for self-contained single-file publish across six runtime identifiers, exposing a sample `RandomNumberTools`.

The target system is a publicly hosted MCP server that serves HexMaster's coding standards. Its content is markdown kept in this same public repository under `/docs`, split into `ADR`, `Designs`, and `Structures`, catalogued by `/docs/index.json`. The server **downloads that content from GitHub at runtime**. It runs as an Azure Container App that scales to zero when idle, provisioned with Bicep and deployed by GitHub Actions.

Constraints that shape this design:

- The template's publish settings (self-contained, single-file, six RIDs) are aimed at desktop/stdio distribution and are wrong for a container image.
- Scale-to-zero means cold starts, and it means any replica may serve any request, so the server must hold no per-session state — and any content cache is per-replica and starts cold.
- Content is fetched from a public repository over the network, so GitHub is a runtime dependency with rate limits and an availability profile the server must tolerate.
- Azure Container Apps needs a container registry that exists *before* the first image push, while the registry itself is provisioned by the Bicep that CD runs — an ordering problem that must be solved without a manual bootstrap step.
- Nothing exists yet, so there is no migration burden and no backwards compatibility to keep.

## Goals / Non-Goals

**Goals:**

- A repository layout that makes the four concerns — content, application code, infrastructure, pipeline — visibly separate and independently reviewable.
- A clean seam between *serving the MCP protocol* (the Mcp project) and *owning the documents* (the Docs project), so protocol wiring and content acquisition evolve independently.
- A content model where `index.json` is unambiguous enough that later changes can build catalog and retrieval tools against it without renegotiating the shape.
- Document retrieval, indexing, and keyword search reachable from unit tests without a web host and without hitting GitHub.
- Infrastructure and pipeline skeletons that actually deploy the placeholder server end-to-end, so the first functional change inherits a working path to production rather than an untested one.

**Non-Goals:**

- MCP tools exposing the document service to clients. The service exists and is tested; the tools folder is created and wired, but empty.
- Authoring real ADRs, designs, or structure documents beyond one template per category.
- Authentication, authorization, rate limiting, or a custom domain on the MCP endpoint.
- Full-text relevance ranking beyond simple keyword scoring, or a search index. No Lucene, no vector search.
- Multi-region or high-availability topology.

## Decisions

### Content is downloaded from GitHub at runtime, not baked into the image

The Docs project is the sole connection to the documents. It fetches them from the public repository at runtime and holds them in memory; the container image ships no content.

*Why:* content and code then release independently. Publishing a new ADR is a merge to `main` that the running server picks up on its next refresh, with no image build, no deployment, and no Azure involvement — which is the point of keeping standards in a public repository people can contribute to.

*Consequence, accepted:* GitHub becomes a runtime dependency. The server cannot serve content it has never successfully fetched, and a cold start after scaling from zero must fetch before it can answer. This is what the caching and health decisions below exist to contain.

*Alternative considered:* copying `/docs` into the container image at build time and reading it from disk. Deterministic, offline, and fast to start — an image tag would pin both code and content. Rejected because every content edit would then require an image build and a deployment, which makes the barrier to contributing a standard far higher than writing the markdown.

### One archive fetch per refresh, not one request per document

A refresh downloads the repository archive (tarball) for the configured ref in a single request and extracts the `docs/` entries into an in-memory document set: the catalog plus every document body.

*Why:* one request per refresh regardless of document count, so unauthenticated GitHub rate limits are a non-issue. Crucially, the catalog and every document body come from the *same commit* — with per-document fetches, an `index.json` read at one moment and a document read a second later can disagree, producing a "document not found" for an entry that plainly exists. Markdown is small, so the whole corpus in memory is cheap and per-document latency drops to zero.

*Alternative considered:* fetching `index.json` on startup and each document lazily from `raw.githubusercontent.com` on first request. Lower cold-start cost and lower memory, but it reintroduces the consistency problem, makes every first request for a document pay network latency, and turns N documents into N requests against an unauthenticated limit.

*Configuration:* the repository owner/name and ref are configuration, defaulting to this repository and `main`. An optional GitHub token is read from configuration and sent when present — not needed for a public repository, but it raises limits and allows the same code to serve a private one.

### The content cache is in-memory, refreshed on a TTL, and never blocks on a failed refresh

The document set is cached in memory per replica, populated on startup and refreshed by a background service on a configurable interval (default 15 minutes). A failed refresh logs and leaves the previously loaded set in place. A refresh that fails when *no* set has ever loaded leaves the server unhealthy.

*Why:* the distinction is what keeps a GitHub outage from being an outage here. Once content is loaded, the server keeps serving it indefinitely, however stale; only a replica that has never loaded anything is genuinely broken, and reporting that through health is what lets Container Apps replace it.

*Trade-off:* content can be up to one TTL stale, and each replica refreshes independently, so two replicas can briefly serve different commits. Acceptable for coding standards, which change on a scale of days.

### Search is a straightforward in-memory keyword scan

Keyword search runs over the cached document set, matching case-insensitively against title, description, tags, and body, and ranks results by where the match landed — title and tag matches above body matches — returning catalog entries rather than full bodies.

*Why:* the corpus is tens of documents held in memory. A scan is microseconds, needs no index to build or invalidate, and stays correct through every refresh for free. Anything more sophisticated would be infrastructure serving a problem this project does not have.

*Alternative considered:* a Lucene.NET index, or embedding-based semantic search. Both are the right answer at a thousand documents and the wrong one at fifty. The service exposes search behind an interface, so replacing the implementation later touches one class.

### Three projects, solution at the repository root

```
HexMaster Coding Standards.slnx        # repo root, per CLAUDE.md
src/HexMaster.CodingStandards.Mcp/     # ASP.NET Core host: MCP protocol, transport, tools, DI
src/HexMaster.CodingStandards.Docs/    # documents: GitHub download, cache, catalog, retrieval, search
tests/HexMaster.CodingStandards.Docs.Tests/   # xUnit v3
```

The Mcp project references Docs; Docs references nothing of the host. The Docs project's GitHub access sits behind an interface so tests exercise retrieval, indexing, and search against a fixture document set with no network. The test project targets **xUnit v3**.

*Why:* content acquisition, caching, and search are where the logic and the bugs are, and none of it needs a `WebApplicationFactory` to test. Keeping it in a library also means the MCP tools added later are thin adapters over an already-tested service. The split is the smallest one that buys this; no separate `Domain`/`Application`/`Infrastructure` layering, which this project does not yet earn.

The solution file moves from `src/` to the repository root, as `CLAUDE.md` already prescribes.

### Container image instead of self-contained single-file publish

The template's `SelfContained`, `PublishSelfContained`, `PublishSingleFile`, and six-RID `RuntimeIdentifiers` are removed. The app becomes a framework-dependent `linux-x64` build in a multi-stage `Dockerfile` on the chiselled ASP.NET runtime base image, listening on port 8080 via `ASPNETCORE_HTTP_PORTS`.

*Why:* the runtime is supplied by the base image, so self-contained publish only inflates the image and lengthens the cold start that scale-to-zero already makes visible. Chiselled base images are small and run as non-root, which suits a public endpoint. With content fetched at runtime, the image holds nothing but the application.

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

A single `prod` environment ships now, in `swedencentral`. The parameter file stays a separate artifact rather than inlined defaults, so adding a `dev.bicepparam` later is additive with no template edit.

`main.bicep` takes a `containerImage` parameter that defaults to a publicly pullable placeholder image. CD runs: **deploy infra**, then **build and push the image to the now-existing registry**, then **deploy infra again with the real image reference**.

*Why:* this makes the stack self-bootstrapping. On a fresh subscription the first deployment creates the registry and an app running the placeholder; the second deployment in the same run swaps in the real image. No manual "run this once" step, and every subsequent run is two idempotent deployments — cheap, because ARM no-ops unchanged resources.

*Alternative considered:* splitting a long-lived `platform.bicep` (registry, environment, workspace, identity) from a per-release `app.bicep`, with the platform deployed on `workflow_dispatch`. Fewer deployments per release and a cleaner conceptual split, but it introduces a bootstrap step a newcomer will forget and two stacks that can disagree. Rejected for a project this size; worth revisiting if platform resources grow.

The container app pulls from the registry using a **user-assigned managed identity** holding `AcrPull`, not registry admin credentials, so no registry password is ever stored in GitHub or in app configuration.

### Scale-to-zero configuration

`minReplicas: 0`, `maxReplicas` parameterised (default 3), with an HTTP scale rule on concurrent requests, on the consumption workload profile.

*Why:* the stated requirement, and the cost model that makes a public side project viable. The accepted cost is a cold start on the first request after an idle period — here, process start *plus* one archive download. No external uptime pinger will be added; that would defeat the point.

### GitHub Actions: two workflows, OIDC authentication

`ci.yml` on pull requests targeting `main` and pushes to `main`: restore, build the solution treating warnings as errors, run the tests, validate `/docs/index.json` against the content tree, and compile the Bicep templates. `cd.yml` on pushes to `main` touching `src/**`, `infra/**`, or the workflow file, plus `workflow_dispatch`.

Note the asymmetry, and that it is deliberate: `docs/**` is validated by CI on every pull request but does **not** trigger CD, because the running server fetches content itself. Content reaches production on the next refresh, not on a deployment.

Azure authentication uses **OIDC federated credentials** on an app registration — no client secret in GitHub. Subscription id, resource group, and environment name come from GitHub Environment variables.

*Why OIDC:* a long-lived service principal secret in a public repository's settings is a standing risk and needs rotation. Federated credentials are scoped to this repository and branch.

*Image tagging:* the commit SHA, plus a moving `latest`. Tagging by SHA is what lets a rollback be "redeploy the previous tag" rather than a rebuild.

## Risks / Trade-offs

- **GitHub is a runtime dependency** → A failed refresh keeps serving the last good content, so an outage degrades freshness rather than availability. Only a replica that never loaded content is unhealthy, and health reporting lets Container Apps replace it. The residual exposure is a cold start during a GitHub outage: the server comes up unable to serve. Accepted; the alternative was baking content into the image.
- **Cold start now includes an archive download** → Accepted as the cost of runtime content. Bounded by fetching one small archive rather than N documents, and by a chiselled base image with framework-dependent publish. If it becomes unacceptable, the levers are a `minReplicas: 1` flip in a parameter file or shipping a fallback copy of `/docs` in the image as a cold-start seed.
- **Content can be one TTL stale, and replicas can disagree** → Acceptable for coding standards. A manual refresh path exists for when it is not.
- **Unauthenticated GitHub rate limits** → Held at one request per replica per TTL by the archive decision, well inside any limit. An optional token raises the ceiling if refreshes ever become frequent.
- **Archive extraction reading outside the intended prefix** → Extraction must confine itself to the archive's `docs/` prefix and reject entries whose resolved path escapes it, including absolute paths, traversal segments, and links. Specified in `document-service` rather than left to the implementation, because an archive from a public repository is untrusted input.
- **In-memory cache grows with the corpus** → Fine at tens of markdown documents; worth revisiting well before thousands. The interface boundary keeps that a contained change.
- **`index.json` drifting from the `/docs` tree** → CI validation fails the build on duplicate ids, missing files, and unindexed documents. Without it, a drifted catalog would reach `main` and the running server would start failing lookups for entries that look valid.
- **Two Bicep deployments per release** → Accepted for self-bootstrapping. Both are idempotent; the cost is seconds.
- **A public MCP endpoint with no auth** → Acceptable for the content served (it is already a public repository), but it leaves the endpoint open to abuse. `maxReplicas` bounds the blast radius on cost. Rate limiting and a Front Door / WAF are a deliberate later decision, not an oversight.
- **Restructuring the template project churns nearly every existing file** → Cheap now, at one commit with no history to preserve; expensive if deferred.
- **`net10.0` plus newer tooling formats (`.slnx`, xUnit v3)** → All supported on current SDKs, but tooling support in older IDEs is thinner. Mitigated by pinning the SDK version in `global.json` so CI and local builds agree.

## Migration Plan

Not applicable in the usual sense — there is no running system and no data. The sequencing that matters is within the change: restructure the solution and get `dotnet build` and `dotnet test` green locally, then build the document service against fixtures, then add the content tree and its validation, then infrastructure, then the pipeline. The pipeline is added last so its first run exercises code that already builds.

Rollback for the deployed app, once the pipeline exists, is redeploying `main.bicep` with the previous image SHA tag. Rolling back *content* is a revert on `main`, picked up on the next refresh.

## Open Questions

- **Resource naming convention** — `hexmaster-codingstandards-prod` for the resource group and a `hexmasterstdprod` style for the registry (which disallows hyphens) will be used unless specified otherwise. Region and environment count are settled: a single `prod` environment in `swedencentral`.
- **The `status` vocabulary for catalog entries** (`draft` / `accepted` / `superseded` / `deprecated`) — the spec fixes that the field is required and constrained to a known set; the exact set is worth confirming, since ADRs and design documents may want different lifecycles.
- **Whether superseded or deprecated documents are served, hidden, or merely flagged** in index listings and search results. The specs currently return them with their status attached and leave filtering to the caller.
- **Refresh TTL** — 15 minutes is a guess that trades staleness against request volume. Worth revisiting once there is a sense of how often standards actually change.
- **Whether the resource group is created by the pipeline** (a subscription-scoped deployment) or pre-created out of band. Resource-group scope is assumed, which means the group must exist before the first CD run.
