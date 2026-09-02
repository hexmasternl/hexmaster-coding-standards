## ADDED Requirements

### Requirement: Content root layout

The repository SHALL contain a content root at `/docs` holding exactly three document category folders: `ADR`, `Designs`, and `Structures`. Every served document SHALL be a markdown file directly inside one of these three folders. No other folder under `/docs` SHALL contain served documents.

#### Scenario: Category folders exist

- **WHEN** the repository is checked out
- **THEN** `docs/ADR`, `docs/Designs`, and `docs/Structures` all exist
- **AND** each contains at least one markdown template document

#### Scenario: A document is placed outside a category folder

- **WHEN** a markdown file is added at `docs/notes.md`, outside the three category folders
- **THEN** catalog validation SHALL fail with a message naming the offending file

### Requirement: Catalog file

The content root SHALL contain a catalog at `/docs/index.json`. The catalog SHALL be a JSON object with a `documents` property holding an array of document entries. Each entry SHALL carry the properties `id`, `title`, `description`, `category`, `status`, `tags`, and `path`, and SHALL carry no other properties.

- `id` SHALL be a non-empty kebab-case string, unique across the catalog, and stable for the life of the document.
- `title` SHALL be a non-empty human-readable string.
- `description` SHALL be a non-empty single-sentence summary of the document's subject.
- `category` SHALL be one of `ADR`, `Design`, or `Structure`.
- `status` SHALL be one of `draft`, `accepted`, `superseded`, or `deprecated`.
- `tags` SHALL be an array of zero or more lowercase kebab-case strings.
- `path` SHALL be a repository-relative POSIX path to an existing markdown file under the category folder matching `category`.

#### Scenario: Catalog is read

- **WHEN** `docs/index.json` is parsed
- **THEN** it deserializes into an object with a `documents` array
- **AND** every entry exposes all seven required properties

#### Scenario: An entry omits a required property

- **WHEN** an entry in `docs/index.json` has no `description`
- **THEN** catalog validation SHALL fail and name the entry's `id` and the missing property

#### Scenario: An entry uses an unknown status

- **WHEN** an entry declares `"status": "in-review"`
- **THEN** catalog validation SHALL fail and report the allowed status values

### Requirement: Catalog and content tree stay consistent

Catalog validation SHALL verify that the catalog and the `/docs` tree agree, and SHALL fail on any disagreement. Validation SHALL detect duplicate `id` values, entries whose `path` does not resolve to an existing file, entries whose `path` sits in a folder that contradicts the entry's `category`, and markdown documents in a category folder that no entry references.

#### Scenario: Two entries share an id

- **WHEN** two entries both declare `"id": "use-bicep-for-infrastructure"`
- **THEN** validation SHALL fail and report the duplicated `id`

#### Scenario: An entry points at a deleted document

- **WHEN** an entry's `path` is `docs/ADR/0001-record-decisions.md` and that file does not exist
- **THEN** validation SHALL fail and report the unresolved path

#### Scenario: Category contradicts the folder

- **WHEN** an entry declares `"category": "ADR"` but its `path` is under `docs/Designs/`
- **THEN** validation SHALL fail and report the mismatch between category and folder

#### Scenario: A document is not indexed

- **WHEN** `docs/Designs/caching-strategy.md` exists and no catalog entry references it
- **THEN** validation SHALL fail and name the unindexed file

#### Scenario: Catalog and tree agree

- **WHEN** every entry resolves to a file in the folder matching its category, all ids are unique, and every markdown document under the three category folders is referenced exactly once
- **THEN** validation SHALL succeed and report the number of documents validated

### Requirement: Document template documents

Each category folder SHALL contain a template document that demonstrates the expected shape of documents in that category, and each template SHALL be registered in the catalog with `status` `draft`. Each document SHALL open with a single level-one markdown heading whose text matches the `title` of its catalog entry.

#### Scenario: Templates are indexed and consistent

- **WHEN** the catalog is validated
- **THEN** each of the three template documents has an entry with `status` `draft`
- **AND** each template's level-one heading text equals its entry's `title`

#### Scenario: Title drifts from the document heading

- **WHEN** a catalog entry's `title` no longer matches the document's level-one heading
- **THEN** validation SHALL report the drift

### Requirement: Catalog maintenance is assisted

The repository SHALL provide an agent skill that updates `/docs/index.json` whenever a document under `/docs` is created, changed, or deleted. The skill SHALL derive the entry's `title`, `description`, `category`, and `tags` from the document's actual content, SHALL preserve an existing entry's `id` when that document is updated, and SHALL remove the corresponding entry when a document is deleted.

#### Scenario: A new document is written

- **WHEN** a new document is added under `docs/ADR/`
- **THEN** the skill appends a catalog entry with a unique kebab-case `id`, a `title` matching the document's level-one heading, a `description` summarising its content, `"category": "ADR"`, and tags drawn from its subject matter

#### Scenario: An existing document is rewritten

- **WHEN** an indexed document's content changes such that its summary or subject matter no longer matches its entry
- **THEN** the skill updates that entry's `title`, `description`, and `tags` in place
- **AND** leaves the entry's `id` unchanged

#### Scenario: A document is deleted

- **WHEN** an indexed document is removed from `/docs`
- **THEN** the skill removes its catalog entry, leaving the remaining entries untouched
