# Recommend skills built from the coding standards

## Why

The server can hand an agent a standard when asked, but nothing prompts an agent to internalise the standards for the repository it is actually working in. In practice that means the standards get consulted when someone remembers they exist, which is not when they matter. Standards that only apply on request are standards that get skipped.

Turning a standard into a durable, always-loaded agent skill is the step that closes this — and it is work the consuming agent is better placed to do than the server, because only the agent can see the codebase. A .NET API repository has no use for a skill about frontend styling variables, and a repository with no message bus has no use for one about eventual consistency. So the server supplies the raw material and the judgement criteria, and the agent decides what is worth writing.

## What Changes

- Add an MCP tool that returns the catalogued documents as skill *candidates* — `id`, `title`, `description`, `category`, `status`, `tags` — together with instructions telling the calling agent how to turn them into skills.
- **The tool recommends; it does not select.** It returns every eligible candidate and instructs the agent to inspect the development environment it is working in and skip documents that do not apply. A skill nobody's code can trigger is noise, and the server cannot tell which those are.
- The response is metadata only. No document body is returned, and the tool makes no per-document request. The agent assesses relevance from the description and tags, then calls the retrieve-by-id tool for the full text of the documents it decides to keep.
- Instructions require each generated skill to carry a concise, trigger-oriented description saying *when* the skill applies, distilled content reflecting the document's actual guidance, and a back-reference naming this MCP server, its retrieve-by-id tool, and the document's `id` — so an agent using the skill later can pull the complete standard when the summary is not enough.
- Instructions are **format-neutral**: they state what each skill must contain and leave the encoding, file format, and location to the consuming client.
- Candidates exclude documents whose `status` is `superseded` or `deprecated`. That is a validity judgement the server can make; relevance is not.
- The instructions tell the agent to skip authoring templates — documents describing the expected shape of a document rather than a standard.
- The server writes nothing, remembers nothing, and tracks no generated skill. The tool is a read of the catalog plus fixed instruction text.

## Capabilities

### New Capabilities
<!-- None. Both affected capabilities already exist. -->

### Modified Capabilities
- `mcp-document-tools`: Adds a fourth tool to the document tool surface — its name, description, absent input schema, the candidate payload, the instruction content it must carry, and how it reports an unloaded catalog and an empty candidate set.
- `document-service`: Adds the skill-candidate set to the service — every catalogued document except those `superseded` or `deprecated`, ordered like every other listing, carrying `status` so the agent can weigh a `draft` standard differently from an `accepted` one.

> **Sequencing.** `document-service` comes from `mcp-server-project-structure` and `mcp-document-tools` from `docs-serve-document-by-id`; both must be archived before this change's deltas apply. The dependency on `docs-serve-document-by-id` is more than spec bookkeeping: the back-reference this change writes into every generated skill names that change's retrieve-by-id tool, so the instructions are inaccurate until it exists. `docs-serve-document-list` and `docs-serve-by-tag` also add tools to `mcp-document-tools`; the additions are disjoint, so whichever archives second rebases its delta.

## Impact

- **Code**: `src/HexMaster.CodingStandards.Mcp/Tools/` gains the recommendation tool and the instruction text it returns, registered in `Program.cs`; `src/HexMaster.CodingStandards.Docs` gains a skill-candidate member on `IDocumentService`.
- **Tests**: `tests/HexMaster.CodingStandards.Docs.Tests` covers the candidate set — exclusions, ordering, `status` carried through, unloaded catalog, and a catalog where everything is excluded. All offline; the candidate set touches no network.
- **Runtime behaviour**: no new GitHub requests, no new caching, no new state. One in-memory read of the cached catalog.
- **Client-visible**: the tool's usefulness rests on instruction text, not on data. That text is a prompt shipped in the server, so changing it changes downstream agent behaviour with no schema change and no signal to clients — it needs reviewing like an interface, not like a string.
- **Follow-on cost to the consuming agent**: the agent makes one retrieve call per document it keeps. Deliberate: it means the token cost lands only on documents that survived the relevance judgement, instead of every document on every call.
- **Docs**: `README.md` gains the tool and a description of the intended workflow. `CLAUDE.md` needs no architecture change.
- **Non-goals**: the server does not write, store, name, version, or update skills; there is no notification when a standard changes after a skill was generated; no category or tag parameter for narrowing; no bodies in the response; no prescribed file format or directory layout; no `docs-index`-style skill for maintaining generated skills.
