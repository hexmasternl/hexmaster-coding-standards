using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// An immutable snapshot of the served content: the catalog plus every document body, all
/// from one commit.
/// </summary>
/// <remarks>
/// Immutability is what makes the cache swap atomic. A refresh builds a whole new set and
/// replaces the reference; a reader that grabbed the old one keeps a consistent view rather
/// than watching entries change underneath it.
/// </remarks>
public sealed class DocumentSet
{
    private readonly Dictionary<string, string> _bodiesById;

    private DocumentSet(
        DocumentCatalog catalog,
        Dictionary<string, string> bodiesById,
        DateTimeOffset loadedAt)
    {
        Catalog = catalog;
        _bodiesById = bodiesById;
        LoadedAt = loadedAt;
    }

    /// <summary>The catalog this set was built from.</summary>
    public DocumentCatalog Catalog { get; }

    /// <summary>When this set was loaded.</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>How many documents can actually be served, bodies included.</summary>
    public int Count => _bodiesById.Count;

    /// <summary>
    /// Builds a set from extracted archive content.
    /// </summary>
    /// <remarks>
    /// An entry whose body is absent from the archive is dropped from the set and logged: a
    /// catalogued id that resolves to nothing would otherwise fail on retrieval with no clue
    /// as to why, and reporting it at load time puts the diagnosis where the cause is.
    /// </remarks>
    /// <exception cref="CatalogFormatException">The archive has no catalog, or it is unparseable.</exception>
    public static DocumentSet FromExtractedContent(
        ExtractedContent content,
        ILogger logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!content.Tree.TryReadText(ContentArchiveExtractor.CatalogPath, out var catalogJson)
            || catalogJson is null)
        {
            throw new CatalogFormatException(
                $"The content archive has no '{ContentArchiveExtractor.CatalogPath}'.");
        }

        var parsed = CatalogParser.Parse(catalogJson);

        foreach (var problem in parsed.Problems)
        {
            logger.LogWarning("Catalog problem: {Problem}", problem.Message);
        }

        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var servable = new List<CatalogEntry>();

        foreach (var entry in parsed.Catalog.Entries)
        {
            if (content.Tree.TryReadText(entry.Path, out var body) && body is not null)
            {
                bodies[entry.Id] = body;
                servable.Add(entry);
                continue;
            }

            logger.LogWarning(
                "Catalog entry '{DocumentId}' points at '{Path}', which the archive does not contain; it will report not-found.",
                entry.Id,
                entry.Path);
        }

        logger.LogInformation(
            "Loaded {DocumentCount} document(s) from the content archive.",
            servable.Count);

        return new DocumentSet(new DocumentCatalog(servable), bodies, timeProvider.GetUtcNow());
    }

    /// <summary>Every document's metadata, ordered by category then id.</summary>
    public IReadOnlyList<DocumentSummary> Index() =>
        Catalog.Entries.Select(DocumentSummary.From).ToArray();

    /// <summary>Retrieves a document by exact id.</summary>
    public Document? Find(string id)
    {
        if (!Catalog.TryGetEntry(id, out var entry) || entry is null)
        {
            return null;
        }

        return _bodiesById.TryGetValue(entry.Id, out var body)
            ? new Document(DocumentSummary.From(entry), body)
            : null;
    }

    /// <summary>
    /// Documents matching a keyword, ranked so metadata matches come before body-only ones.
    /// </summary>
    /// <remarks>
    /// A linear scan over tens of in-memory documents costs microseconds and stays correct
    /// through every refresh with nothing to invalidate, which is why there is no index here.
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
    /// How well an entry matches: title and tag hits outrank description, which outranks the
    /// body. A document whose title is the keyword is almost always the one wanted.
    /// </summary>
    private int RankOf(CatalogEntry entry, string keyword)
    {
        const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

        if (entry.Title.Contains(keyword, Comparison))
        {
            return 4;
        }

        if (entry.Tags.Any(tag => tag.Contains(keyword, Comparison)))
        {
            return 3;
        }

        if (entry.Description.Contains(keyword, Comparison))
        {
            return 2;
        }

        return _bodiesById.TryGetValue(entry.Id, out var body) && body.Contains(keyword, Comparison)
            ? 1
            : 0;
    }
}
