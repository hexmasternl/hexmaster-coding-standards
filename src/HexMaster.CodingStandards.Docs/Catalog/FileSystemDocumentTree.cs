namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// A document tree backed by the working copy on disk, rooted at a repository root. This is
/// what CI and local validation run against; the server validates the documents it
/// downloaded instead.
/// </summary>
public sealed class FileSystemDocumentTree : IDocumentTree
{
    private readonly string _repositoryRoot;

    /// <summary>Creates a tree over the repository at <paramref name="repositoryRoot"/>.</summary>
    public FileSystemDocumentTree(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = System.IO.Path.GetFullPath(repositoryRoot);
        MarkdownPaths = EnumerateMarkdownPaths(_repositoryRoot);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> MarkdownPaths { get; }

    /// <inheritdoc />
    public bool TryReadText(string path, out string? text)
    {
        text = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var absolute = System.IO.Path.GetFullPath(System.IO.Path.Combine(_repositoryRoot, path));

        // A path from the catalog is untrusted input; confine reads to the repository root
        // even though the validator rejects traversal separately.
        if (!absolute.StartsWith(_repositoryRoot, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(absolute))
        {
            return false;
        }

        text = File.ReadAllText(absolute);
        return true;
    }

    private static string[] EnumerateMarkdownPaths(string repositoryRoot)
    {
        var contentRoot = System.IO.Path.Combine(repositoryRoot, DocumentCategories.ContentRoot);
        if (!Directory.Exists(contentRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(contentRoot, "*.md", SearchOption.AllDirectories)
            .Select(absolute => System.IO.Path
                .GetRelativePath(repositoryRoot, absolute)
                .Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
