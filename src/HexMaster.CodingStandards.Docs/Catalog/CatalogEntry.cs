namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// One validated document entry from <c>docs/index.json</c>. Reaching this type means every
/// required property was present and every constrained value was recognised - invalid
/// entries are reported as <see cref="CatalogProblem"/>s and never become an entry.
/// </summary>
/// <param name="Id">Stable kebab-case handle, unique across the catalog.</param>
/// <param name="Title">Matches the document's level-one heading.</param>
/// <param name="Description">One sentence saying what the document decides or describes.</param>
/// <param name="Category">Which of the three kinds of document this is.</param>
/// <param name="Status">Where the document sits in its lifecycle.</param>
/// <param name="Tags">Lowercase kebab-case subject tags; empty rather than null.</param>
/// <param name="Path">Repository-relative POSIX path to the markdown file.</param>
public sealed record CatalogEntry(
    string Id,
    string Title,
    string Description,
    DocumentCategory Category,
    DocumentStatus Status,
    IReadOnlyList<string> Tags,
    string Path);
