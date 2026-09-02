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
    /// <summary>Shortest tag the substring fallback will run for.</summary>
    private const int MinimumFallbackLength = 2;

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
    /// Selects documents by an already-normalised tag: an exact pass, and a substring pass
    /// only when the exact pass found nothing.
    /// </summary>
    /// <remarks>
    /// The two passes are never merged. A query that hits a real tag is never diluted by
    /// near misses - <c>ci</c> returns the <c>ci</c> documents and never reaches the
    /// substring pass, where <c>cicd</c> would be waiting.
    ///
    /// Ordering comes from <see cref="DocumentCatalog"/>, which sorts on construction, so
    /// the scan yields category-then-id order without sorting here and independently of the
    /// order matches were found. Each entry is tested once, so a document carrying several
    /// satisfying tags is returned once. Entries the parser rejected never reached the
    /// catalog, so they cannot match.
    /// </remarks>
    /// <param name="normalisedTag">A trimmed, lowercased, non-empty tag.</param>
    public TagSelection SelectByTag(string normalisedTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalisedTag);

        // OrdinalIgnoreCase over an already-lowercased input is the lowercased-and-ordinal
        // comparison the catalog's kebab-case tags need, without a ToLower per tag.
        var exact = Matching(tag => tag.Equals(normalisedTag, StringComparison.OrdinalIgnoreCase));

        if (exact.Count > 0)
        {
            return new TagSelection(normalisedTag, TagMatchKind.Exact, exact);
        }

        // One character is contained in nearly every kebab-case tag, so the fallback would
        // return the whole catalog dressed up as a narrowing - the worst possible answer,
        // because it looks like a result. Below two characters there is no signal.
        if (normalisedTag.Length < MinimumFallbackLength)
        {
            return new TagSelection(normalisedTag, TagMatchKind.None, []);
        }

        var fallback = Matching(tag => tag.Contains(normalisedTag, StringComparison.OrdinalIgnoreCase));

        return fallback.Count > 0
            ? new TagSelection(normalisedTag, TagMatchKind.Fallback, fallback)
            : new TagSelection(normalisedTag, TagMatchKind.None, []);
    }

    /// <summary>Every document with at least one tag satisfying the predicate, in catalog order.</summary>
    private IReadOnlyList<DocumentSummary> Matching(Func<string, bool> tagPredicate) =>
        Catalog.Entries
            .Where(entry => entry.Tags.Any(tagPredicate))
            .Select(DocumentSummary.From)
            .ToArray();

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
