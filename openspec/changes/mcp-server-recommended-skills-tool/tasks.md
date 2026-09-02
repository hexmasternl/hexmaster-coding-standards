# Tasks — Recommend skills built from the coding standards

> Prerequisite: `mcp-server-project-structure` (creates the document service) and `docs-serve-document-by-id` (creates the `mcp-document-tools` surface and the retrieve-by-id tool this change's instructions name) must be implemented and archived first. If `docs-serve-document-list` or `docs-serve-by-tag` archives before this change, rebase these deltas onto the archived specs.

## 1. Service: the candidate set

- [x] 1.1 Add a skill-candidate member to `IDocumentService` returning ordered `DocumentSummary` entries wrapped in the existing result type, so an unloaded catalog reports `NotReady` rather than an empty list
- [x] 1.2 Implement eligibility: exclude `superseded` and `deprecated`, include `accepted` and `draft`, and let no other property affect the outcome
- [x] 1.3 Skip entries the catalog rejected as invalid, so they can neither appear nor fail the request
- [x] 1.4 Order by `category`, then `id`, independent of catalog order
- [x] 1.5 Confirm the member reads the cached catalog only and performs no network work and no body read

## 2. Service tests

- [x] 2.1 Test that superseded and deprecated documents are excluded and that accepted and draft documents are included, with `draft` reported as the status
- [x] 2.2 Test that category, tags, and subject matter never exclude a document
- [x] 2.3 Test ordering is by category then id and is identical across two requests over the same catalog
- [x] 2.4 Test one entry per eligible document with no duplicates, and that an invalid entry is omitted without failing the request
- [x] 2.5 Test an all-excluded catalog returns an empty set as success, an unloaded catalog reports `NotReady`, and a stale cached catalog succeeds
- [x] 2.6 Confirm every test runs over a fixture catalog with no HTTP handler, no host, and no network

## 3. The instruction text

- [x] 3.1 Write the instruction text as a reviewed constant in the host project, ordered as a procedure: assess the environment, decide per candidate, retrieve the kept documents, then write each skill
- [x] 3.2 Instruct the agent to inspect the development environment it is working in — languages, frameworks, project layout, and which concerns the codebase actually has — before writing anything
- [x] 3.3 Instruct the agent to skip candidates that do not apply to that environment, stating that a skill for a standard the codebase cannot exercise should not be written
- [x] 3.4 Instruct the agent to skip candidates describing how to author a document rather than stating a standard, and to treat a `draft` candidate as provisional
- [x] 3.5 Instruct the agent to call the retrieve-by-id tool for each kept candidate, naming that tool, and state that this response carries no document bodies
- [x] 3.6 State the four mandatory elements of a generated skill: an identifier derived from the document, a concise description saying when the skill applies, content distilled from the document's actual guidance, and the back-reference
- [x] 3.7 Specify the back-reference concretely — this MCP server, its retrieve-by-id tool, and the document's `id` — and say it exists so a later agent can retrieve the complete document when the distilled content is insufficient
- [x] 3.8 State that the skill's encoding and location follow the consuming client's own convention, naming no file format, extension, frontmatter schema, or directory path, while keeping the content elements mandatory
- [x] 3.9 Review the text as an interface, not a string: it determines what downstream agents write into other repositories and changes behaviour with no schema change and no signal to clients

## 4. The MCP tool

- [x] 4.1 Add the recommendation tool to `src/HexMaster.CodingStandards.Mcp/Tools/` with no input parameters, named consistently with the other document tools
- [x] 4.2 Write the tool description so an agent can distinguish it from the retrieval, listing, and tag tools without calling it, stating that it returns skill candidates and authoring instructions rather than document content
- [x] 4.3 Project each candidate to exactly `id`, `title`, `description`, `category`, `status`, and `tags` — no body, no `path`, no GitHub URL — returning `tags` as an empty array when absent
- [x] 4.4 Combine the candidates with the instruction text into one response, returning every candidate the service reports with no relevance filtering of its own
- [x] 4.5 Return an empty candidate set as a success stating there is nothing to generate, and a tool error identifying the catalog as unavailable when nothing has ever loaded
- [x] 4.6 Report failures as tool results flagged `isError` rather than protocol errors, consistent with the sibling tools
- [x] 4.7 Register the tool in `Program.cs` at the existing composition seam, changing no other host file
- [x] 4.8 Verify the tool applies no eligibility rule, no filtering, and no sorting of its own, and depends only on the document service interface

## 5. Tool tests

- [x] 5.1 Test the projection carries exactly the six fields and excludes bodies, paths, and repository references
- [x] 5.2 Test that every candidate the service reports appears in the response, with none suppressed by subject matter
- [x] 5.3 Test the response's load-bearing instruction elements are present — environment assessment, skip-if-irrelevant, skip templates, draft is provisional, retrieve kept candidates, the four skill elements, the back-reference naming the retrieve tool, and format delegation — asserting on presence rather than exact wording so editorial changes do not break the suite
- [x] 5.4 Test that the instructions name no file format, extension, frontmatter schema, or directory path
- [x] 5.5 Test empty-candidate success, unloaded-catalog error, and that two invocations over the same catalog return identical responses

## 6. End-to-end verification and documentation

- [x] 6.1 Run the server against the real catalog and confirm an MCP client sees the tool listed with an empty input schema alongside the other document tools
- [x] 6.2 Confirm the response carries every eligible document — the three authoring templates included, since they are excluded by instruction rather than by rule — and no superseded or deprecated document
- [x] 6.3 Drive the full workflow with a real agent in a .NET repository: confirm it skips candidates that do not apply, retrieves only the documents it keeps, and writes skills carrying all four required elements
- [x] 6.4 Confirm in that run that the generated back-references are actionable — following one retrieves the complete document
- [x] 6.5 Add the tool and the intended workflow to `README.md`, including that the server writes and tracks nothing and that generated skills are not invalidated when a standard changes
- [x] 6.6 Run `dotnet build` and `dotnet test` from the repository root with no network access and confirm a clean, warning-free build and a passing offline suite
