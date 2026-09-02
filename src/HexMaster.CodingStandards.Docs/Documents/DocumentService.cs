using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Serves documents: the catalog from memory, bodies from the per-document cache.
/// </summary>
public sealed class DocumentService : IDocumentService
{
    private const string NotReadyMessage =
        "The coding standards have not been loaded yet; the server could not reach GitHub. Retry shortly.";

    private readonly DocumentSetCache _catalogCache;
    private readonly DocumentBodyCache _bodyCache;
    private readonly ILogger<DocumentService> _logger;

    /// <summary>Creates the service over the catalog and body caches.</summary>
    public DocumentService(
        DocumentSetCache catalogCache,
        DocumentBodyCache bodyCache,
        ILogger<DocumentService> logger)
    {
        _catalogCache = catalogCache;
        _bodyCache = bodyCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsReady => _catalogCache.HasContent;

    /// <inheritdoc />
    public DocumentResult<IReadOnlyList<DocumentSummary>> GetIndex()
    {
        var set = _catalogCache.Current;

        return set is null
            ? DocumentResult<IReadOnlyList<DocumentSummary>>.NotReady(NotReadyMessage)
            : DocumentResult<IReadOnlyList<DocumentSummary>>.Success(set.Index());
    }

    /// <inheritdoc />
    public async Task<DocumentResult<Document>> GetDocumentAsync(string id, CancellationToken cancellationToken)
    {
        var set = _catalogCache.Current;

        if (set is null)
        {
            return DocumentResult<Document>.NotReady(NotReadyMessage);
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return DocumentResult<Document>.InvalidRequest("A document id is required.");
        }

        var entry = set.FindEntry(id);

        // No fuzzy fallback, and no network call: an id is a handle, and quietly returning a
        // near match would hand a caller a different standard than the one it asked for.
        if (entry is null)
        {
            return DocumentResult<Document>.NotFound($"No document has id '{id}'.");
        }

        if (!ContentPath.IsValid(entry.Path, out var reason))
        {
            _logger.LogWarning(
                "Refusing to fetch document '{DocumentId}': its catalog path '{Path}' is unusable because {Reason}.",
                entry.Id,
                entry.Path,
                reason);

            return DocumentResult<Document>.Unavailable(
                $"Document '{id}' is catalogued but its path is not usable, so its content cannot be retrieved.");
        }

        var fetch = await _bodyCache.GetAsync(entry.Path, cancellationToken).ConfigureAwait(false);

        if (!fetch.Result.IsSuccess)
        {
            _logger.LogWarning(
                "Could not fetch the body of '{DocumentId}' from '{Path}': {Reason}",
                entry.Id,
                entry.Path,
                fetch.Result.Reason);

            return DocumentResult<Document>.Unavailable(
                $"Document '{id}' is catalogued but its content could not be retrieved: {fetch.Result.Reason}.");
        }

        return DocumentResult<Document>.Success(new Document(
            DocumentSummary.From(entry),
            fetch.Result.Content!,
            fetch.FetchedAt));
    }

    /// <inheritdoc />
    public DocumentResult<IReadOnlyList<DocumentSummary>> Search(string keyword)
    {
        var set = _catalogCache.Current;

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
