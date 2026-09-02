---
name: docs-index
description: Maintains /docs/index.json, the catalog the MCP server serves. Use whenever a document under /docs is written, updated, renamed, moved, or deleted — including ADRs, design documents, and file/project structure documents — so the catalog entry's title, description, category, status, and tags always match the document's actual content. Also use when the user asks to fix, validate, or reconcile index.json, or reports that the catalog is out of sync with the docs tree.
license: MIT
metadata:
  author: HexMaster
  version: "1.0"
---

# Maintaining `/docs/index.json`

`/docs/index.json` is the **authoritative catalog** of every document this MCP server serves. The server does not crawl folders — a document that is missing from the catalog is invisible, and an entry pointing at a missing file is a runtime failure. CI fails the build when the catalog and the tree disagree, so the catalog must be updated in the same change as the document.

Your job is to read what the document actually says and write an entry that would let someone choose that document from the catalog alone, without opening it.

## When to act

Act on any change to a file under `docs/ADR/`, `docs/Designs/`, or `docs/Structures/`:

| Change | What you do |
| --- | --- |
| New document | Append an entry |
| Content edited | Re-derive `title`, `description`, `tags`; update `status` if the document says so |
| Renamed or moved | Update `path` (and `category` if the folder changed); **keep the `id`** |
| Deleted | Remove the entry |

Do this **after** the document is written, so you index what is actually on disk, not what you intended to write. Do not touch entries for documents your change did not affect.

## The entry schema

Every entry has exactly these seven properties, in this order, and no others:

```json
{
  "id": "use-bicep-for-infrastructure",
  "title": "Use Bicep for infrastructure as code",
  "description": "Records the decision to declare all Azure resources in Bicep rather than Terraform or portal-managed resources, and why.",
  "category": "ADR",
  "status": "accepted",
  "tags": ["infrastructure", "bicep", "azure", "iac"],
  "path": "docs/ADR/0003-use-bicep-for-infrastructure.md"
}
```

Entries live in the top-level `documents` array.

### `id`

Kebab-case, unique across the catalog, and **permanent**. It is the handle MCP clients use to fetch a document, so changing one silently breaks every reference to it.

- Derive a new id from the document's subject, not its filename number: `0003-use-bicep-for-infrastructure.md` → `use-bicep-for-infrastructure`.
- When a document is edited, renamed, or moved, the id **never** changes.
- Before adding one, check the existing ids. On a collision, disambiguate by subject (`caching-strategy-api` vs `caching-strategy-docs`), never with a numeric suffix.

### `title`

Must match the document's level-one heading exactly — CI checks this. If the heading is a poor title, fix the heading and the entry together rather than letting them drift.

### `description`

One sentence, and the property that earns its keep. It is what a client sees when deciding whether to open the document, so it must say **what this document decides or describes**, not what topic it belongs to.

- Good: "Records the decision to declare all Azure resources in Bicep rather than Terraform, and why portal-managed resources were rejected."
- Bad: "An ADR about infrastructure." (says nothing the other fields do not)
- Bad: "This document describes our approach to infrastructure as code and contains background, alternatives considered, consequences, and related decisions." (structure, not substance)

Write it from the document's content, in the present tense, without repeating the title verbatim.

### `category`

One of `ADR`, `Design`, or `Structure` — singular, and it must agree with the folder:

| Folder | `category` | Holds |
| --- | --- | --- |
| `docs/ADR/` | `ADR` | Architecture decision records: a decision, its context, its alternatives, its consequences |
| `docs/Designs/` | `Design` | Coding designs, patterns, and conventions to follow |
| `docs/Structures/` | `Structure` | File, folder, and project structure standards |

If a document sits in a folder that contradicts its subject, say so rather than quietly indexing it — moving it is usually the right fix.

### `status`

One of `draft`, `accepted`, `superseded`, `deprecated`. Take it from the document: ADRs normally state their own status, and if a document says it is superseded, the entry says `superseded` too. A new document that has not been agreed on is `draft`. Never promote `draft` to `accepted` on your own initiative — that is an editorial decision, so ask.

When a document is superseded by another, set the old entry to `superseded` in the same change that adds the new one.

### `tags`

Lowercase kebab-case, typically three to six, chosen so a client filtering by tag finds this document.

- Reuse tags already in the catalog before inventing new ones — read the existing tag vocabulary first. A tag used once is noise.
- Tag the **subject**, not the category: `bicep`, `azure`, `naming-conventions`, `testing`. Never `adr`, `design`, `structure`, `documentation` — `category` already carries that.
- Skip tags that apply to every document in the repository.

### `path`

Repository-relative, forward slashes, pointing at the actual file: `docs/Designs/error-handling.md`. Not absolute, no `./` prefix, no backslashes — even on Windows.

## Procedure

1. **Read the document in full.** The description and tags must come from its content. Skimming the heading produces exactly the vacuous descriptions this catalog exists to avoid.
2. **Read `docs/index.json`** to see the existing ids and tag vocabulary.
3. **Apply the change** from the table above, keeping the `documents` array sorted by `category` then `id` so diffs stay readable.
4. **Verify before finishing:**
   - The JSON parses, is 2-space indented, and has no trailing commas.
   - Every id is unique; the touched entry's id is unchanged if the document already existed.
   - Every `path` resolves to a file that exists, and its folder matches its `category`.
   - Every markdown file under the three category folders is referenced by exactly one entry, and no entry points at a deleted file.
   - `title` equals the document's level-one heading.
5. **Report** which entries you added, changed, or removed, and why the description says what it says.

## Guardrails

- Never invent an entry for a document that does not exist on disk, and never leave an entry behind for one you deleted.
- Never change an existing `id`.
- Do not reformat or reorder entries you did not touch — keep the diff to the change at hand.
- If the document's content genuinely does not fit `ADR`, `Design`, or `Structure`, stop and ask rather than forcing a category.
