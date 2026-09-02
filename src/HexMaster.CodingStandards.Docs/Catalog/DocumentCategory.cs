namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// The three kinds of document this repository serves. The catalog spells these in the
/// singular ("Design"), while the folder holding them is plural ("Designs") - see
/// <see cref="DocumentCategories.FolderFor"/>.
/// </summary>
public enum DocumentCategory
{
    /// <summary>An architecture decision record.</summary>
    Adr,

    /// <summary>A coding design, pattern, or convention.</summary>
    Design,

    /// <summary>A file, folder, or project structure standard.</summary>
    Structure
}

/// <summary>
/// Conversions between a category, the string used in the catalog, and the folder that holds
/// documents of that category. Parsing is deliberately strict: an unrecognised value is a
/// problem to report, never a value to guess at.
/// </summary>
public static class DocumentCategories
{
    /// <summary>The content root every document lives under.</summary>
    public const string ContentRoot = "docs";

    private static readonly (DocumentCategory Category, string CatalogValue, string Folder)[] Map =
    [
        (DocumentCategory.Adr, "ADR", "ADR"),
        (DocumentCategory.Design, "Design", "Designs"),
        (DocumentCategory.Structure, "Structure", "Structures")
    ];

    /// <summary>Every category, in catalog sort order.</summary>
    public static IReadOnlyList<DocumentCategory> All { get; } =
        Map.Select(entry => entry.Category).ToArray();

    /// <summary>The values a catalog entry's <c>category</c> may take, for error messages.</summary>
    public static IReadOnlyList<string> AllowedCatalogValues { get; } =
        Map.Select(entry => entry.CatalogValue).ToArray();

    /// <summary>The catalog spelling of a category, for example <c>Design</c>.</summary>
    public static string CatalogValueFor(DocumentCategory category) =>
        Map.First(entry => entry.Category == category).CatalogValue;

    /// <summary>
    /// The repository-relative folder holding a category's documents, for example
    /// <c>docs/Designs</c>.
    /// </summary>
    public static string FolderFor(DocumentCategory category) =>
        $"{ContentRoot}/{Map.First(entry => entry.Category == category).Folder}";

    /// <summary>
    /// Resolves a catalog <c>category</c> value. Matching is exact and case-sensitive, so a
    /// drifted spelling is reported rather than silently accepted.
    /// </summary>
    public static bool TryParse(string? catalogValue, out DocumentCategory category)
    {
        foreach (var entry in Map)
        {
            if (string.Equals(entry.CatalogValue, catalogValue, StringComparison.Ordinal))
            {
                category = entry.Category;
                return true;
            }
        }

        category = default;
        return false;
    }

    /// <summary>
    /// The category whose folder holds the given repository-relative document path, or
    /// <c>null</c> when the path is not directly inside a category folder.
    /// </summary>
    public static DocumentCategory? CategoryOfFolder(string documentPath)
    {
        foreach (var category in All)
        {
            var folder = FolderFor(category) + "/";
            if (!documentPath.StartsWith(folder, StringComparison.Ordinal))
            {
                continue;
            }

            // Only documents directly inside the category folder count; a nested folder is
            // not a category and its contents are not served.
            var remainder = documentPath[folder.Length..];
            if (!remainder.Contains('/', StringComparison.Ordinal))
            {
                return category;
            }
        }

        return null;
    }
}
