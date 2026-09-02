# Design — Recommend skills built from the coding standards

## Context

Three tools on this surface answer questions: retrieve a document, list the catalog, find by tag. This one is different in kind — it answers a question nobody asked, by telling the calling agent to go and do something. Its payload is mostly instruction text, and its value is almost entirely in how well that text steers a model.

That difference is worth naming up front, because it changes what "correct" means. The other tools are correct when they return the right rows. This one is correct when an agent that calls it ends up with a set of skills that are relevant, accurate, and traceable back to the standards — an outcome the server cannot observe and cannot enforce. What the server *can* do is supply complete, honest raw material and criteria sharp enough to act on.

What is settled and inherited:

- Tools project and format; the document service owns document logic and is testable with no host and no network.
- Failures reach the client as tool results flagged `isError`. An empty result is a success; a catalog that has never loaded is a failure.
- The catalog carries `id`, `title`, `description`, `category`, `status`, `tags` for every document, and is the authoritative metadata source.
- Retrieval by id already exists, which is what makes a back-reference in a generated skill actionable rather than decorative.

The catalog today holds 19 documents: 12 ADRs, 4 Designs, 3 Structures, of which 4 are `draft` — three of those are authoring templates and one is a genuine standard still being settled.

## Goals / Non-Goals

**Goals:**

- One MCP tool returning every eligible document as a skill candidate, with metadata sufficient to judge relevance and nothing more.
- Instructions that make the agent decide what to write, against its own repository, rather than writing one skill per document reflexively.
- Every generated skill carries a concise trigger-oriented description, distilled content, and a back-reference to this server and the document `id`.
- Instructions portable across agent clients, prescribing content rather than encoding.
- The eligibility rule lives in the document service and is unit-testable offline.

**Non-Goals:**

- Writing, storing, naming, versioning, or updating skills. The server has no filesystem access to the consumer and no memory of what it recommended.
- Detecting that a standard changed after a skill was written, or invalidating stale skills.
- Any prescribed file format, frontmatter schema, or directory layout.
- Narrowing parameters — category, tag, or status filters. The agent filters, and it filters on relevance, which no parameter can express.
- Returning document bodies.

## Decisions

### The server recommends candidates; the agent decides relevance

The tool returns every eligible document and instructs the agent to inspect the repository it is working in — languages, frameworks, project layout, whether there is a frontend, an API, a message bus, a database — and to skip candidates that do not apply.

This is the central decision, and it is the user's: only the agent can see the codebase. A skill for centralised frontend styling variables in a repository with no frontend is not merely useless, it is actively harmful — it dilutes the skill set, competes for the model's attention, and trains the agent to ignore skill descriptions because some of them never match. Filtering server-side would need the server to know the consumer's stack, which it cannot and should not.

The corollary is that the tool must return *everything* eligible. A server that pre-filtered on its own guess would remove the agent's ability to make the judgement it is being asked to make.

### Metadata only, bodies fetched per kept document

The response carries `id`, `title`, `description`, `category`, `status`, `tags` — no markdown. The agent judges relevance from the description and tags, then calls the retrieve-by-id tool for the full text of each document it decided to keep.

The catalog's descriptions are a full sentence of real substance, written to summarise what a document decides, so they are adequate for a relevance judgement in a way a bare title would not be. Returning all 19 bodies would put tens of kilobytes into the response, most of it about documents the agent is going to discard — paying the largest cost for the least useful content, and growing without bound as the catalog does.

Fetching per kept document also exercises exactly the path the generated skills will reference, so an agent that writes the back-reference has already used it once and knows it works.

*Alternative considered.* An `includeBodies` parameter would cover a caller that prefers one large response to N calls. Two response shapes to specify, test, and document, for a caller who has not appeared; if one does, it is additive.

### Instructions are format-neutral

The instructions state what each skill must contain — an identifier, a concise description saying *when* it applies, distilled content reflecting the document's guidance, and the back-reference — and say nothing about `SKILL.md`, YAML frontmatter, file extensions, or directory paths.

The consuming agent knows its own client's skill format; the server does not, and a server that guesses wrong produces output the client cannot load. Naming a format would also date the tool against every client that changes its own conventions.

The cost is real: output varies between clients, and a weaker model given only content requirements may produce something its client cannot use. Mitigated by making the *content* requirements specific and non-negotiable — the variance is then in encoding, which the client's own agent is competent at, rather than in substance.

### Every skill carries a back-reference to the server and the id

The instructions require each generated skill to name this MCP server, its retrieve-by-id tool, and the source document's `id`, phrased so a later agent knows it can pull the complete standard when the distilled content is not enough.

Without this a skill is a lossy copy that silently drifts as the standard changes, with no way back to the source. With it, the skill becomes an index into the authoritative document: cheap to keep loaded, and one call away from the full text when the question gets specific. This is also what makes distillation safe — the skill is allowed to be incomplete precisely because completeness is retrievable.

### Eligibility excludes `superseded` and `deprecated`; templates are handled in the instructions

The service's candidate set omits documents whose `status` is `superseded` or `deprecated`. That is a validity judgement about the document itself, unrelated to the consumer's environment, and getting it wrong means writing a skill that teaches a retracted standard.

`draft` documents are *included*, carrying their status, because a draft can be a real standard still being settled — `pragmatic-domain-driven-design` is one today — and the agent is told to weigh a draft as provisional rather than skip it.

Authoring templates are excluded by instruction rather than by rule. They are `draft`, so status cannot separate them from a genuine draft standard, and matching on an id or title pattern would be a brittle rule encoding a naming convention the catalog does not guarantee. Their titles are literally "ADR template", "Design template", "Structure template" and their descriptions open with "The expected shape of…", so an agent told to skip documents that describe how to write a document will not miss them.

*If this ever needs to be reliable rather than probable*, the fix is an explicit catalog property marking a document as a template, which would let the service exclude them by rule. That changes the catalog schema and the `docs-content-structure` spec, so it stays out of this change.

### Ordering by category, then id

Same as every other listing. Repeated calls return identical payloads, so a client can diff them, and an agent working through the list moves through ADRs, then Designs, then Structures rather than an arbitrary order.

### The instruction text is an interface

The text lives in the host project alongside the tool. It is a prompt shipped inside a server, and it determines what downstream agents write into other people's repositories — a change to it changes behaviour everywhere with no version bump, no schema change, and no signal to any client.

Treating it as a string constant is how it rots. It gets reviewed like an interface, and the tests assert that its load-bearing elements are present — the environment-relevance instruction, the four required skill elements, the back-reference naming the retrieve tool — rather than asserting on its exact wording, which would make every editorial improvement a test failure.

## Risks / Trade-offs

- **The tool's effect is unobservable and unenforceable.** The server cannot tell whether the agent skipped the relevance step, wrote a skill per document, or omitted the back-reference. → Accepted; it is inherent to instructing another agent. Reduced by making instructions specific and ordered — assess environment, then decide, then fetch, then write — rather than a list of qualities. What the server *can* guarantee is the accuracy of the candidate data, which the specs cover.
- **Generated skills go stale silently.** A skill written today reflects the standard as of today; the standard can change tomorrow and nothing tells the skill's owner. → The back-reference is the mitigation: the skill points at the live document, so an agent that follows it gets current content. Genuine invalidation needs change notification and generation tracking — a real gap, and explicitly out of scope.
- **Format neutrality can produce unusable output.** An agent given content requirements but no encoding may write something its client cannot load. → Content requirements are specific and mandatory; encoding is delegated to the party that actually knows it. The alternative — prescribing a format — fails harder and more often, for every client that does not use it.
- **Templates are excluded by instruction, not by rule.** A model that ignores the instruction writes a skill teaching how to format an ADR. → Low harm and self-evident in review; the reliable fix costs a catalog schema change and is recorded above.
- **Relevance judgement is only as good as the description.** An agent skipping a document on a misleading one-sentence description never learns what it missed, and the miss is invisible. → Catalog descriptions are maintained by the `docs-index` skill from actual document content, which is where the leverage is. Tags give a second signal for the same judgement.
- **Four in-flight changes now edit `mcp-document-tools`.** → Additions are disjoint requirements; sequencing is recorded in the proposal. The hard ordering constraint is `docs-serve-document-by-id`, because the instructions name its tool.
