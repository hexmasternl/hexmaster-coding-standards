using HexMaster.CodingStandards.Docs.Catalog;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// One document's metadata, without its body. This is what listings and search results are
/// made of.
/// </summary>
/// <param name="Id">Stable handle for retrieving the document.</param>
/// <param name="Title">The document's title.</param>
/// <param name="Description">One sentence on what it decides or describes.</param>
/// <param name="Category">Which of the three kinds of document this is.</param>
/// <param name="Status">Where it sits in its lifecycle.</param>
/// <param name="Tags">Subject tags.</param>
public sealed record DocumentSummary(
    string Id,
    string Title,
    string Description,
    DocumentCategory Category,
    DocumentStatus Status,
    IReadOnlyList<string> Tags)
{
    internal static DocumentSummary From(CatalogEntry entry) => new(
        entry.Id,
        entry.Title,
        entry.Description,
        entry.Category,
        entry.Status,
        entry.Tags);
}

/// <summary>A document's metadata together with its full markdown body.</summary>
/// <param name="Summary">The document's metadata.</param>
/// <param name="Markdown">The document's full markdown text.</param>
public sealed record Document(DocumentSummary Summary, string Markdown);

/// <summary>
/// Why a document request did not return a document.
/// </summary>
public enum DocumentOutcome
{
    /// <summary>The request succeeded.</summary>
    Success,

    /// <summary>No catalog entry has that id.</summary>
    NotFound,

    /// <summary>
    /// No content has ever loaded, so the answer is unknown rather than empty. Distinct from
    /// <see cref="NotFound"/>: a caller told "not ready" should retry, one told "not found"
    /// should not.
    /// </summary>
    NotReady,

    /// <summary>The request itself was not valid, for example a blank search keyword.</summary>
    InvalidRequest
}

/// <summary>
/// The result of a document request, carrying the outcome so an empty answer is never
/// confused with a failure.
/// </summary>
/// <typeparam name="T">The value returned on success.</typeparam>
/// <param name="Outcome">Whether the request succeeded, and if not, why.</param>
/// <param name="Value">The value, present only when <paramref name="Outcome"/> is success.</param>
/// <param name="Message">Detail for a non-success outcome, suitable for a caller to read.</param>
public sealed record DocumentResult<T>(DocumentOutcome Outcome, T? Value, string? Message = null)
{
    /// <summary>Whether the request succeeded.</summary>
    public bool IsSuccess => Outcome == DocumentOutcome.Success;

    internal static DocumentResult<T> Success(T value) => new(DocumentOutcome.Success, value);

    internal static DocumentResult<T> NotFound(string message) =>
        new(DocumentOutcome.NotFound, default, message);

    internal static DocumentResult<T> NotReady(string message) =>
        new(DocumentOutcome.NotReady, default, message);

    internal static DocumentResult<T> InvalidRequest(string message) =>
        new(DocumentOutcome.InvalidRequest, default, message);
}

/// <summary>
/// Reads the coding standards this server serves.
/// </summary>
/// <remarks>
/// Every method answers from content already downloaded and cached, so none of them performs
/// network work. When no content has ever loaded they report
/// <see cref="DocumentOutcome.NotReady"/> rather than an empty result - "there are no
/// documents" and "I cannot reach GitHub" mean different things to a caller.
/// </remarks>
public interface IDocumentService
{
    /// <summary>Whether content has loaded and the service can answer.</summary>
    bool IsReady { get; }

    /// <summary>Lists every catalogued document, ordered by category then id.</summary>
    DocumentResult<IReadOnlyList<DocumentSummary>> GetIndex();

    /// <summary>Retrieves a document and its body by exact, case-sensitive id.</summary>
    DocumentResult<Document> GetDocument(string id);

    /// <summary>
    /// Searches title, description, tags, and body for a keyword, case-insensitively,
    /// ranking metadata matches above body-only matches.
    /// </summary>
    DocumentResult<IReadOnlyList<DocumentSummary>> Search(string keyword);
}
