namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// A document tree held in memory, keyed by repository-relative POSIX path. This is what the
/// running server serves from once it has downloaded the documents, and what tests use in
/// place of the filesystem.
/// </summary>
public sealed class InMemoryDocumentTree : IDocumentTree
{
    private readonly Dictionary<string, string> _documents;

    /// <summary>An empty tree.</summary>
    public static InMemoryDocumentTree Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Creates a tree over the given path-to-text pairs.</summary>
    public InMemoryDocumentTree(IEnumerable<KeyValuePair<string, string>> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        _documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in documents)
        {
            _documents[path] = text;
        }

        MarkdownPaths = _documents.Keys
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> MarkdownPaths { get; }

    /// <inheritdoc />
    public bool TryReadText(string path, out string? text)
    {
        if (string.IsNullOrEmpty(path))
        {
            text = null;
            return false;
        }

        return _documents.TryGetValue(path, out text);
    }
}
