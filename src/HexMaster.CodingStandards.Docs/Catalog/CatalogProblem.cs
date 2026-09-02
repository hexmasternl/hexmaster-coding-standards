namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// The kinds of disagreement that can exist between <c>docs/index.json</c> and the document
/// tree. The runtime logs these and serves what is valid; CI treats any of them as a build
/// failure, which is what keeps a drifted catalog off <c>main</c>.
/// </summary>
public enum CatalogProblemKind
{
    /// <summary>A required property was absent or blank.</summary>
    MissingProperty,

    /// <summary>A <c>category</c> value outside the allowed set.</summary>
    UnknownCategory,

    /// <summary>A <c>status</c> value outside the allowed set.</summary>
    UnknownStatus,

    /// <summary>Two entries declared the same <c>id</c>.</summary>
    DuplicateId,

    /// <summary>An entry's <c>path</c> does not resolve to a document.</summary>
    UnresolvedPath,

    /// <summary>An entry's <c>path</c> sits in a folder that contradicts its <c>category</c>.</summary>
    CategoryFolderMismatch,

    /// <summary>A document exists in a category folder that no entry references.</summary>
    UnindexedDocument,

    /// <summary>A markdown document sits outside the three category folders.</summary>
    DocumentOutsideCategoryFolder,

    /// <summary>An entry's <c>title</c> no longer matches the document's level-one heading.</summary>
    TitleHeadingDrift,

    /// <summary>An entry's <c>path</c> escapes the content root, or is not a markdown file.</summary>
    InvalidPath
}

/// <summary>
/// One catalog problem, carrying enough detail to fix it without opening the validator:
/// which entry or file, and what is wrong.
/// </summary>
/// <param name="Kind">What sort of problem this is.</param>
/// <param name="Message">Human-readable description naming the entry or file.</param>
/// <param name="EntryId">The offending entry's id, when the problem is entry-scoped.</param>
/// <param name="Path">The offending path, when the problem is file-scoped.</param>
public sealed record CatalogProblem(
    CatalogProblemKind Kind,
    string Message,
    string? EntryId = null,
    string? Path = null)
{
    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Message}";
}
