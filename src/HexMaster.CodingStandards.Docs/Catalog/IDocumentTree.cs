namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// A set of document files addressed by repository-relative POSIX path, for example
/// <c>docs/ADR/0000-adr-template.md</c>.
/// </summary>
/// <remarks>
/// Two things implement this. CI validates the working tree through
/// <see cref="FileSystemDocumentTree"/>; the running server validates and serves the
/// documents it downloaded from GitHub. Having both behind one interface is what lets the
/// same validation rules run in CI and at load time.
/// </remarks>
public interface IDocumentTree
{
    /// <summary>
    /// Every markdown document in the tree, as repository-relative POSIX paths. Includes
    /// documents outside the category folders, so validation can report them.
    /// </summary>
    IReadOnlyCollection<string> MarkdownPaths { get; }

    /// <summary>
    /// Reads a document's text. Returns <c>false</c> when the path is not in the tree.
    /// </summary>
    bool TryReadText(string path, out string? text);
}
