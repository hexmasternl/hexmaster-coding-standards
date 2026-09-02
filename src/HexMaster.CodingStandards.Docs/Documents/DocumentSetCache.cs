namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Holds the current <see cref="DocumentSet"/> and swaps it atomically on load.
/// </summary>
/// <remarks>
/// The whole cache is one reference to an immutable set, so a load is a single volatile
/// write. Readers either see the old set or the new one; there is no window in which a
/// reader can observe a half-loaded catalog, and no lock on the read path.
/// </remarks>
public sealed class DocumentSetCache
{
    private DocumentSet? _current;

    /// <summary>The loaded set, or <c>null</c> when none has ever loaded.</summary>
    public DocumentSet? Current => Volatile.Read(ref _current);

    /// <summary>Whether content has loaded and the service can answer.</summary>
    public bool HasContent => Current is not null;

    /// <summary>Replaces the cached set.</summary>
    public void Replace(DocumentSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Volatile.Write(ref _current, set);
    }

    /// <summary>
    /// Whether the cached catalog has aged past <paramref name="lifetime"/>, measured from
    /// when it was loaded.
    /// </summary>
    /// <remarks>
    /// A cache holding nothing counts as expired: the caller's next step is the same either
    /// way, which is to load. Absolute from the load instant, not sliding - a busy replica
    /// must not be able to keep a stale catalog alive by reading it.
    /// </remarks>
    public bool IsExpired(DateTimeOffset now, TimeSpan lifetime)
    {
        var set = Current;
        return set is null || now - set.LoadedAt >= lifetime;
    }
}
