using HexMaster.CodingStandards.Docs.Catalog;
using Microsoft.Extensions.Logging;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// An immutable snapshot of the catalog: which documents exist and what they are about.
/// </summary>
/// <remarks>
/// Holds no bodies. Bodies are fetched per document and cached in
/// <see cref="DocumentBodyCache"/>, so this snapshot is small and cheap to replace.
///
/// Immutability is what makes the refresh swap atomic: a refresh builds a whole new set and
/// replaces the reference, so a reader that grabbed the old one keeps a consistent view
/// rather than watching entries change underneath it.
/// </remarks>
public sealed class DocumentSet
{
    private DocumentSet(DocumentCatalog catalog, DateTimeOffset loadedAt)
    {
        Catalog = catalog;
        LoadedAt = loadedAt;
    }

    /// <summary>The catalog this set was built from.</summary>
    public DocumentCatalog Catalog { get; }

    /// <summary>When this set was loaded.</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>How many documents the catalog lists.</summary>
    public int Count => Catalog.Count;

    /// <summary>
    /// Builds a set from catalog JSON, logging every invalid entry it skips.
    /// </summary>
    /// <exception cref="CatalogFormatException">The catalog could not be parsed.</exception>
    public static DocumentSet FromCatalogJson(string catalogJson, ILogger logger, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogJson);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var parsed = CatalogParser.Parse(catalogJson);

        foreach (var problem in parsed.Problems)
        {
            logger.LogWarning("Catalog problem: {Problem}", problem.Message);
        }

        // An entry whose path could never be fetched is reported once, at load time, rather
        // than becoming a puzzling per-request failure later. It stays in the catalog so the
        // document remains listed and reports its body as unavailable when asked for.
        foreach (var entry in parsed.Catalog.Entries)
        {
            if (!ContentPath.IsValid(entry.Path, out var reason))
            {
                logger.LogWarning(
                    "Catalog entry '{DocumentId}' has an unusable path '{Path}': {Reason}. Its body will report as unavailable.",
                    entry.Id,
                    entry.Path,
                    reason);
            }
        }

        logger.LogInformation("Loaded a catalog of {DocumentCount} document(s).", parsed.Catalog.Count);

        return new DocumentSet(parsed.Catalog, timeProvider.GetUtcNow());
    }

    /// <summary>Every document's metadata, ordered by category then id.</summary>
    public IReadOnlyList<DocumentSummary> Index() =>
        Catalog.Entries.Select(DocumentSummary.From).ToArray();

    /// <summary>
    /// Every document as a listing entry, ordered by category then id.
    /// </summary>
    /// <remarks>
    /// Ordering comes from <see cref="DocumentCatalog"/>, which sorts on construction, so two
    /// listings over the same set are identical without sorting here. Entries the parser
    /// rejected never reached the catalog, so they cannot appear.
    /// </remarks>
    public IReadOnlyList<DocumentListEntry> Listing() =>
        Catalog.Entries.Select(entry => DocumentListEntry.From(DocumentSummary.From(entry))).ToArray();

    /// <summary>Looks up a catalog entry by exact, case-sensitive id.</summary>
    public CatalogEntry? FindEntry(string id) =>
        Catalog.TryGetEntry(id, out var entry) ? entry : null;

    /// <summary>
    /// Documents whose metadata matches a keyword, ranked with title matches first, then
    /// tags, then description.
    /// </summary>
    /// <remarks>
    /// A linear scan over tens of in-memory entries costs microseconds and needs no index to
    /// build or invalidate.
    /// </remarks>
    public IReadOnlyList<DocumentSummary> Search(string keyword)
    {
        var matches = new List<(int Rank, CatalogEntry Entry)>();

        foreach (var entry in Catalog.Entries)
        {
            var rank = RankOf(entry, keyword);
            if (rank > 0)
            {
                matches.Add((rank, entry));
            }
        }

        return matches
            .OrderByDescending(match => match.Rank)
            .ThenBy(match => match.Entry.Category)
            .ThenBy(match => match.Entry.Id, StringComparer.Ordinal)
            .Select(match => DocumentSummary.From(match.Entry))
            .ToArray();
    }

    /// <summary>
    /// How well an entry matches: a title hit outranks a tag hit, which outranks a
    /// description hit. A document whose title is the keyword is almost always the one wanted.
    /// </summary>
    private static int RankOf(CatalogEntry entry, string keyword)
    {
        const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

        if (entry.Title.Contains(keyword, Comparison))
        {
            return 3;
        }

        if (entry.Tags.Any(tag => tag.Contains(keyword, Comparison)))
        {
            return 2;
        }

        return entry.Description.Contains(keyword, Comparison) ? 1 : 0;
    }
}
