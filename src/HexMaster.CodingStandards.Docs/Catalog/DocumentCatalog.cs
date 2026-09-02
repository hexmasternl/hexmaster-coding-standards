namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// The parsed, valid contents of <c>docs/index.json</c>. Entries are ordered by category then
/// id, so anything projected from a catalog is deterministically ordered for free.
/// </summary>
public sealed class DocumentCatalog
{
    private readonly Dictionary<string, CatalogEntry> _byId;

    /// <summary>An empty catalog. Distinct from "no catalog loaded", which is not a catalog at all.</summary>
    public static DocumentCatalog Empty { get; } = new([]);

    /// <summary>Creates a catalog from entries, ordering them by category then id.</summary>
    public DocumentCatalog(IEnumerable<CatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Entries = entries
            .OrderBy(entry => entry.Category)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();

        // Ordinal so id lookup is exact and case-sensitive: an id is a handle clients quote
        // back to us, and matching it loosely would make two distinct ids collide.
        _byId = Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
    }

    /// <summary>Every valid entry, ordered by category then id.</summary>
    public IReadOnlyList<CatalogEntry> Entries { get; }

    /// <summary>How many documents the catalog lists.</summary>
    public int Count => Entries.Count;

    /// <summary>Looks up an entry by its exact id.</summary>
    public bool TryGetEntry(string id, out CatalogEntry? entry)
    {
        if (string.IsNullOrEmpty(id))
        {
            entry = null;
            return false;
        }

        return _byId.TryGetValue(id, out entry);
    }
}
