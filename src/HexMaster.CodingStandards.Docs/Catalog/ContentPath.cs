namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// Validates and encodes a catalog <c>path</c> before it becomes part of a GitHub request.
/// </summary>
/// <remarks>
/// The catalog is downloaded from a public repository, so its <c>path</c> values are
/// untrusted input. This is the successor to the archive extractor's <c>docs/</c>-prefix
/// confinement: the old risk was writing outside a directory, the new one is a crafted path
/// steering a request at a different repository path or a different host entirely. The check
/// is cheap and belongs on the boundary.
/// </remarks>
public static class ContentPath
{
    /// <summary>
    /// Checks that a catalog path is a relative POSIX path to a markdown file directly inside
    /// one of the three category folders under the content root.
    /// </summary>
    /// <param name="path">The catalog entry's <c>path</c> value.</param>
    /// <param name="reason">Why the path was rejected, when it was.</param>
    public static bool IsValid(string? path, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "the path is empty";
            return false;
        }

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            reason = "the path contains a backslash; catalog paths are POSIX paths";
            return false;
        }

        // Catches a scheme ("https://"), a protocol-relative host ("//host/"), and a Windows
        // drive qualifier ("C:/") in one check - none of which may reach a request URI.
        if (path.Contains("://", StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal)
            || path.StartsWith('/')
            || path.Contains("//", StringComparison.Ordinal))
        {
            reason = "the path is absolute, or carries a scheme or host";
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            reason = "the path is absolute";
            return false;
        }

        var segments = path.Split('/');
        if (Array.Exists(segments, segment => segment is ".." or "." or ""))
        {
            reason = "the path contains a traversal or empty segment";
            return false;
        }

        if (DocumentCategories.CategoryOfFolder(path) is null)
        {
            reason =
                $"the path is not directly inside {string.Join(", ", DocumentCategories.All.Select(DocumentCategories.FolderFor))}";
            return false;
        }

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            reason = "the path does not name a markdown file";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Encodes a validated path for use in a GitHub Contents API request, segment by segment
    /// so the separators survive and spaces or other reserved characters round-trip.
    /// </summary>
    public static string Encode(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}
