namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// Where a document sits in its lifecycle. Served documents carry their status so a caller
/// can tell a current standard from a retired one.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Written, not yet agreed.</summary>
    Draft,

    /// <summary>Agreed and in force.</summary>
    Accepted,

    /// <summary>Replaced by a later document, kept for the trail.</summary>
    Superseded,

    /// <summary>No longer to be followed, with no replacement.</summary>
    Deprecated
}

/// <summary>
/// Conversions between a status and its catalog spelling. Parsing is strict for the same
/// reason as <see cref="DocumentCategories"/>: an unknown lifecycle value is a problem to
/// report, not one to guess at.
/// </summary>
public static class DocumentStatuses
{
    private static readonly (DocumentStatus Status, string CatalogValue)[] Map =
    [
        (DocumentStatus.Draft, "draft"),
        (DocumentStatus.Accepted, "accepted"),
        (DocumentStatus.Superseded, "superseded"),
        (DocumentStatus.Deprecated, "deprecated")
    ];

    /// <summary>The values a catalog entry's <c>status</c> may take, for error messages.</summary>
    public static IReadOnlyList<string> AllowedCatalogValues { get; } =
        Map.Select(entry => entry.CatalogValue).ToArray();

    /// <summary>The catalog spelling of a status, for example <c>accepted</c>.</summary>
    public static string CatalogValueFor(DocumentStatus status) =>
        Map.First(entry => entry.Status == status).CatalogValue;

    /// <summary>Resolves a catalog <c>status</c> value, exactly and case-sensitively.</summary>
    public static bool TryParse(string? catalogValue, out DocumentStatus status)
    {
        foreach (var entry in Map)
        {
            if (string.Equals(entry.CatalogValue, catalogValue, StringComparison.Ordinal))
            {
                status = entry.Status;
                return true;
            }
        }

        status = default;
        return false;
    }
}
