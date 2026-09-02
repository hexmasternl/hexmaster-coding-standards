namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// The outcome of cross-checking a catalog against a document tree.
/// </summary>
/// <param name="Problems">Every disagreement found. Empty means catalog and tree agree.</param>
/// <param name="DocumentsValidated">How many documents were checked, for a useful success message.</param>
public sealed record CatalogValidationResult(
    IReadOnlyList<CatalogProblem> Problems,
    int DocumentsValidated)
{
    /// <summary>True when the catalog and the tree agree on everything.</summary>
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Checks that <c>docs/index.json</c> and the document tree describe the same set of
/// documents.
/// </summary>
/// <remarks>
/// The server does not crawl folders, so a document missing from the catalog is invisible and
/// an entry pointing at a missing file fails at runtime. Both are cheap to detect and
/// expensive to discover in production, which is why this runs in CI and fails the build.
/// </remarks>
public static class CatalogValidator
{
    /// <summary>
    /// Validates a parsed catalog against a document tree. Pass the parse result's problems
    /// in as <paramref name="parseProblems"/> so a caller gets one combined verdict.
    /// </summary>
    public static CatalogValidationResult Validate(
        DocumentCatalog catalog,
        IDocumentTree tree,
        IReadOnlyList<CatalogProblem>? parseProblems = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(tree);

        var problems = new List<CatalogProblem>(parseProblems ?? []);
        var indexedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in catalog.Entries)
        {
            ValidateEntry(entry, tree, problems, indexedPaths);
        }

        foreach (var path in tree.MarkdownPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            ValidateTreeDocument(path, indexedPaths, problems);
        }

        return new CatalogValidationResult(problems, catalog.Count);
    }

    private static void ValidateEntry(
        CatalogEntry entry,
        IDocumentTree tree,
        List<CatalogProblem> problems,
        HashSet<string> indexedPaths)
    {
        var path = entry.Path;

        if (!IsWellFormedDocumentPath(path))
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.InvalidPath,
                $"Entry '{entry.Id}' has path '{path}'; it must be a repository-relative POSIX path to a markdown file under '{DocumentCategories.ContentRoot}/'.",
                entry.Id,
                path));
            return;
        }

        indexedPaths.Add(path);

        var folderCategory = DocumentCategories.CategoryOfFolder(path);
        if (folderCategory is null)
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.CategoryFolderMismatch,
                $"Entry '{entry.Id}' has path '{path}', which is not directly inside a category folder.",
                entry.Id,
                path));
        }
        else if (folderCategory != entry.Category)
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.CategoryFolderMismatch,
                $"Entry '{entry.Id}' declares category '{DocumentCategories.CatalogValueFor(entry.Category)}' but sits in '{DocumentCategories.FolderFor(folderCategory.Value)}'.",
                entry.Id,
                path));
        }

        if (!tree.TryReadText(path, out var text) || text is null)
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.UnresolvedPath,
                $"Entry '{entry.Id}' points at '{path}', which does not exist.",
                entry.Id,
                path));
            return;
        }

        var heading = ReadLevelOneHeading(text);
        if (heading is null)
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.TitleHeadingDrift,
                $"Document '{path}' has no level-one heading to match entry '{entry.Id}' title '{entry.Title}'.",
                entry.Id,
                path));
        }
        else if (!string.Equals(heading, entry.Title, StringComparison.Ordinal))
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.TitleHeadingDrift,
                $"Entry '{entry.Id}' has title '{entry.Title}' but '{path}' opens with heading '{heading}'.",
                entry.Id,
                path));
        }
    }

    private static void ValidateTreeDocument(
        string path,
        HashSet<string> indexedPaths,
        List<CatalogProblem> problems)
    {
        if (DocumentCategories.CategoryOfFolder(path) is null)
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.DocumentOutsideCategoryFolder,
                $"'{path}' is a markdown document outside the category folders; served documents live directly in {string.Join(", ", DocumentCategories.All.Select(DocumentCategories.FolderFor))}.",
                Path: path));
            return;
        }

        if (!indexedPaths.Contains(path))
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.UnindexedDocument,
                $"'{path}' is not referenced by any catalog entry, so the server would never serve it.",
                Path: path));
        }
    }

    /// <summary>
    /// A catalog path must stay inside the content root and name a markdown file. Rejecting
    /// traversal segments, absolute paths, and backslashes here means the value is safe to
    /// use as a lookup key or to interpolate into a URL later.
    /// </summary>
    private static bool IsWellFormedDocumentPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith(DocumentCategories.ContentRoot + "/", StringComparison.Ordinal)
        && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Contains("..", StringComparison.Ordinal)
        && !path.Contains("//", StringComparison.Ordinal)
        && !System.IO.Path.IsPathRooted(path);

    /// <summary>
    /// The text of the document's first level-one heading, ignoring anything before it.
    /// Stops at the first one: a document has exactly one title.
    /// </summary>
    public static string? ReadLevelOneHeading(string documentText)
    {
        ArgumentNullException.ThrowIfNull(documentText);

        foreach (var line in documentText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return null;
    }
}
