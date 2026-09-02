using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using HexMaster.CodingStandards.Docs.Catalog;
using Microsoft.Extensions.Logging;

namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// What was taken out of a content archive: the documents plus the catalog file, and the
/// entries that were refused.
/// </summary>
/// <param name="Tree">Documents and <c>docs/index.json</c>, keyed by repository-relative path.</param>
/// <param name="RejectedEntries">Archive entry names that were skipped, with the reason.</param>
public sealed record ExtractedContent(
    InMemoryDocumentTree Tree,
    IReadOnlyList<string> RejectedEntries);

/// <summary>
/// Reads the <c>docs/</c> portion of a GitHub repository tarball into memory.
/// </summary>
/// <remarks>
/// An archive from a public repository is untrusted input, so extraction is confined to the
/// <c>docs/</c> prefix and every entry whose resolved path escapes it is refused. Nothing is
/// written to disk, which removes the classic tar-slip write primitive outright; the checks
/// remain because a path that escapes the prefix must not even be read into the served set.
/// </remarks>
public static class ContentArchiveExtractor
{
    /// <summary>Repository-relative path of the catalog inside the archive.</summary>
    public const string CatalogPath = "docs/index.json";

    /// <summary>
    /// Extracts documents and the catalog from a gzipped tarball. Refused entries are logged
    /// individually and skipped; they never fail the extraction.
    /// </summary>
    public static async Task<ExtractedContent> ExtractAsync(
        Stream archive,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return await ReadEntriesAsync(archive, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException)
        {
            // A truncated or corrupt download is an upstream problem, not a content problem.
            // Translating it here is what lets a failed refresh fall back to the previously
            // loaded content instead of surfacing as an unhandled stream error.
            throw new ContentUnavailableException(
                "The content archive downloaded from GitHub is truncated or not a valid gzipped tarball.",
                exception);
        }
    }

    private static async Task<ExtractedContent> ReadEntriesAsync(
        Stream archive,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        var rejected = new List<string>();

        await using var decompressed = new GZipStream(archive, CompressionMode.Decompress);
        await using var reader = new TarReader(decompressed);

        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false)
               is { } entry)
        {
            if (!ShouldConsider(entry, logger, rejected))
            {
                continue;
            }

            if (!TryResolveContentPath(entry.Name, out var path))
            {
                // Either outside docs/, or an attempt to escape it. The two are logged
                // differently because one is routine and the other is worth noticing.
                if (LooksLikeEscapeAttempt(entry.Name))
                {
                    logger.LogWarning(
                        "Refusing archive entry '{EntryName}': its path escapes the '{Prefix}' prefix.",
                        entry.Name,
                        DocumentCategories.ContentRoot);
                    rejected.Add(entry.Name);
                }

                continue;
            }

            if (entry.DataStream is null)
            {
                continue;
            }

            using var textReader = new StreamReader(entry.DataStream, Encoding.UTF8);
            documents[path] = await textReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ExtractedContent(new InMemoryDocumentTree(documents), rejected);
    }

    private static bool ShouldConsider(TarEntry entry, ILogger logger, List<string> rejected)
    {
        switch (entry.EntryType)
        {
            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
                return true;

            case TarEntryType.SymbolicLink:
            case TarEntryType.HardLink:
                // A link's target is resolved outside this process's control, so its content
                // cannot be vouched for even when the link itself sits inside docs/.
                logger.LogWarning(
                    "Refusing archive entry '{EntryName}': links are not followed.",
                    entry.Name);
                rejected.Add(entry.Name);
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Maps an archive entry name onto a repository-relative content path.
    /// </summary>
    /// <remarks>
    /// GitHub wraps a tarball in one top-level folder named for the repository and commit,
    /// so the first segment is stripped before anything is checked. Whatever remains must
    /// sit inside the content root, with no traversal, no absolute path, and no drive
    /// qualifier.
    /// </remarks>
    private static bool TryResolveContentPath(string entryName, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        var normalized = entryName.Replace('\\', '/');

        var firstSeparator = normalized.IndexOf('/', StringComparison.Ordinal);
        if (firstSeparator < 0)
        {
            return false;
        }

        var relative = normalized[(firstSeparator + 1)..];
        var prefix = DocumentCategories.ContentRoot + "/";

        if (!relative.StartsWith(prefix, StringComparison.Ordinal)
            || relative.Contains("..", StringComparison.Ordinal)
            || relative.Contains("//", StringComparison.Ordinal)
            || relative.Contains(':', StringComparison.Ordinal)
            || relative.StartsWith('/')
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        path = relative;
        return true;
    }

    /// <summary>
    /// Whether an entry that was not accepted looks like an escape attempt rather than an
    /// ordinary file from elsewhere in the repository, so the log stays useful.
    /// </summary>
    private static bool LooksLikeEscapeAttempt(string entryName)
    {
        var normalized = entryName.Replace('\\', '/');

        return normalized.Contains("..", StringComparison.Ordinal)
            || normalized.StartsWith('/')
            || normalized.Contains(':', StringComparison.Ordinal)
            || entryName.Contains('\\', StringComparison.Ordinal);
    }
}
