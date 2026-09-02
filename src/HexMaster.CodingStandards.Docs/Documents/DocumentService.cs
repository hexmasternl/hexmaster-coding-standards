using HexMaster.CodingStandards.Docs.Catalog;
using Microsoft.Extensions.Logging;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Serves documents: the catalog from memory, bodies from the per-document cache.
/// </summary>
/// <remarks>
/// Every read first asks <see cref="CatalogLoader"/> for a current catalog. Inside the cache
/// window that is a timestamp comparison and nothing else; past it, the read pays one small
/// fetch. That is the whole cost of having no background refresh timer.
/// </remarks>
public sealed class DocumentService : IDocumentService
{
    private const string NotReadyMessage =
        "The coding standards have not been loaded yet; the server could not reach GitHub. Retry shortly.";

    private readonly DocumentSetCache _catalogCache;
    private readonly DocumentBodyCache _bodyCache;
    private readonly CatalogLoader _catalogLoader;
    private readonly ILogger<DocumentService> _logger;

    /// <summary>Creates the service over the catalog loader and the two caches.</summary>
    public DocumentService(
        DocumentSetCache catalogCache,
        DocumentBodyCache bodyCache,
        CatalogLoader catalogLoader,
        ILogger<DocumentService> logger)
    {
        _catalogCache = catalogCache;
        _bodyCache = bodyCache;
        _catalogLoader = catalogLoader;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsReady => _catalogCache.HasContent;

    /// <inheritdoc />
    public async Task<DocumentResult<IReadOnlyList<DocumentSummary>>> GetIndexAsync(
        CancellationToken cancellationToken)
    {
        var set = await CurrentSetAsync(cancellationToken).ConfigureAwait(false);

        return set is null
            ? DocumentResult<IReadOnlyList<DocumentSummary>>.NotReady(NotReadyMessage)
            : DocumentResult<IReadOnlyList<DocumentSummary>>.Success(set.Index());
    }

    /// <inheritdoc />
    public async Task<DocumentResult<IReadOnlyList<DocumentListEntry>>> GetListingAsync(
        CancellationToken cancellationToken)
    {
        var set = await CurrentSetAsync(cancellationToken).ConfigureAwait(false);

        // An empty catalog is a success: "there are no standards" is an answer. Only a
        // catalog that never loaded is a failure, because then we do not know.
        return set is null
            ? DocumentResult<IReadOnlyList<DocumentListEntry>>.NotReady(NotReadyMessage)
            : DocumentResult<IReadOnlyList<DocumentListEntry>>.Success(set.Listing());
    }

    /// <inheritdoc />
    public async Task<DocumentResult<Document>> GetDocumentAsync(string id, CancellationToken cancellationToken)
    {
        var set = await CurrentSetAsync(cancellationToken).ConfigureAwait(false);

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
    public async Task<DocumentResult<IReadOnlyList<DocumentSummary>>> SearchAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        var set = await CurrentSetAsync(cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc />
    public async Task<DocumentResult<TagSelection>> FindByTagAsync(
        string tag,
        CancellationToken cancellationToken)
    {
        var set = await CurrentSetAsync(cancellationToken).ConfigureAwait(false);

        // A cached catalog whose last refresh failed is served as it stands; only a catalog
        // that never loaded is a failure, because "nothing is tagged that way" and "I cannot
        // tell you what is tagged" are different answers.
        if (set is null)
        {
            return DocumentResult<TagSelection>.NotReady(NotReadyMessage);
        }

        // Rejected rather than treated as "match everything": a blank tag would otherwise
        // return the whole catalog and look like a successful narrowing.
        if (string.IsNullOrWhiteSpace(tag))
        {
            return DocumentResult<TagSelection>.InvalidRequest(
                "A tag is required. To list every document, use the listing instead.");
        }

        // Normalising here keeps the matching rules in one place: the set compares, the
        // caller supplies a tag as a human wrote it.
        return DocumentResult<TagSelection>.Success(set.SelectByTag(tag.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// Refreshes the catalog if it has aged out, then returns whatever is cached.
    /// </summary>
    /// <remarks>
    /// Reads the cache after the loader returns rather than taking the loader's word for it:
    /// a reload that failed over an already-cached catalog leaves the old one in place, and
    /// serving that stale catalog is the correct answer, not an error.
    /// </remarks>
    private async Task<DocumentSet?> CurrentSetAsync(CancellationToken cancellationToken)
    {
        await _catalogLoader.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
        return _catalogCache.Current;
    }
}
