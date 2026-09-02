namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Serves documents from the cached content set.
/// </summary>
/// <remarks>
/// Every method is a read against an immutable snapshot, so this type is thread-safe and
/// does no I/O. Acquiring and refreshing the content is
/// <see cref="ContentRefreshService"/>'s job.
/// </remarks>
public sealed class DocumentService : IDocumentService
{
    private const string NotReadyMessage =
        "The coding standards have not been loaded yet; the server could not reach GitHub. Retry shortly.";

    private readonly DocumentSetCache _cache;

    /// <summary>Creates the service over a content cache.</summary>
    public DocumentService(DocumentSetCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public bool IsReady => _cache.HasContent;

    /// <inheritdoc />
    public DocumentResult<IReadOnlyList<DocumentSummary>> GetIndex()
    {
        var set = _cache.Current;

        return set is null
            ? DocumentResult<IReadOnlyList<DocumentSummary>>.NotReady(NotReadyMessage)
            : DocumentResult<IReadOnlyList<DocumentSummary>>.Success(set.Index());
    }

    /// <inheritdoc />
    public DocumentResult<Document> GetDocument(string id)
    {
        var set = _cache.Current;

        if (set is null)
        {
            return DocumentResult<Document>.NotReady(NotReadyMessage);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return DocumentResult<Document>.InvalidRequest("A document id is required.");
        }

        var document = set.Find(id);

        // No fuzzy fallback: an id is a handle, and quietly returning a near match would
        // hand a caller a different standard than the one it asked for.
        return document is null
            ? DocumentResult<Document>.NotFound($"No document has id '{id}'.")
            : DocumentResult<Document>.Success(document);
    }

    /// <inheritdoc />
    public DocumentResult<IReadOnlyList<DocumentSummary>> Search(string keyword)
    {
        var set = _cache.Current;

        if (set is null)
        {
            return DocumentResult<IReadOnlyList<DocumentSummary>>.NotReady(NotReadyMessage);
        }

        // Rejected rather than treated as "match everything": a blank keyword is a caller
        // bug, and answering it with the whole catalog hides that.
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return DocumentResult<IReadOnlyList<DocumentSummary>>.InvalidRequest(
                "A search keyword is required. To list every document, use the index instead.");
        }

        return DocumentResult<IReadOnlyList<DocumentSummary>>.Success(set.Search(keyword.Trim()));
    }
}
