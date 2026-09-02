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

/// <summary>
/// One document as it appears in a listing: enough to choose a document, and nothing more.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="DocumentSummary"/>, which keeps <c>Status</c>. The
/// listing is what an agent reads to orient itself, and the five fields here are what it
/// needs to pick one document out of the set without fetching any of them.
/// </remarks>
/// <param name="Id">Stable handle for retrieving the document.</param>
/// <param name="Title">The document's title.</param>
/// <param name="Category">Which of the three kinds of document this is.</param>
/// <param name="Description">One sentence on what it decides or describes.</param>
/// <param name="Tags">Subject tags; empty rather than absent when the document has none.</param>
public sealed record DocumentListEntry(
    string Id,
    string Title,
    DocumentCategory Category,
    string Description,
    IReadOnlyList<string> Tags)
{
    /// <summary>Projects an index entry down to the five listing fields.</summary>
    public static DocumentListEntry From(DocumentSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new DocumentListEntry(
            summary.Id,
            summary.Title,
            summary.Category,
            summary.Description,
            summary.Tags);
    }
}

/// <summary>
/// How a tag selection found the documents it returned.
/// </summary>
/// <remarks>
/// Carried on the result rather than on each entry: match quality is a property of the
/// selection, not of a document, and a caller that cannot tell an exact hit from an
/// approximate one will treat the tag it guessed as one the catalog actually uses.
/// </remarks>
public enum TagMatchKind
{
    /// <summary>At least one document carries the requested tag, and only those are returned.</summary>
    Exact,

    /// <summary>
    /// No document carries the requested tag, so documents having a tag that contains it are
    /// returned instead.
    /// </summary>
    Fallback,

    /// <summary>Neither pass matched. A successful, empty answer - nothing is tagged that way.</summary>
    None
}

/// <summary>
/// The documents a tag selected, together with how they were found.
/// </summary>
/// <param name="Tag">The normalised tag that was selected on - trimmed and lowercased.</param>
/// <param name="Match">Whether the documents carry the tag exactly, approximately, or at all.</param>
/// <param name="Documents">The matching documents, ordered by category then id; empty when nothing matched.</param>
public sealed record TagSelection(
    string Tag,
    TagMatchKind Match,
    IReadOnlyList<DocumentSummary> Documents);

/// <summary>A document's metadata together with its full markdown body.</summary>
/// <param name="Summary">The document's metadata.</param>
/// <param name="Markdown">The document's full markdown text.</param>
/// <param name="FetchedAt">
/// When the body was fetched from GitHub. Exposed because a body can be up to one cache
/// lifetime older than the catalog entry describing it.
/// </param>
public sealed record Document(DocumentSummary Summary, string Markdown, DateTimeOffset FetchedAt);

/// <summary>
/// Why a document request did not return a document.
/// </summary>
public enum DocumentOutcome
{
    /// <summary>The request succeeded.</summary>
    Success,

    /// <summary>
    /// No catalog entry has that id. Deterministic and the caller's mistake; no network call
    /// was made.
    /// </summary>
    NotFound,

    /// <summary>
    /// The document is catalogued but its body could not be obtained - missing at the ref,
    /// rate-limited, or a failed request. Kept distinct from <see cref="NotFound"/> because
    /// retrying later may work, whereas retrying an unknown id never will.
    /// </summary>
    Unavailable,

    /// <summary>
    /// No catalog has ever loaded, so the answer is unknown rather than empty. A caller told
    /// "not ready" should retry; one told "not found" should not.
    /// </summary>
    NotReady,

    /// <summary>The request itself was not valid, for example a blank search keyword.</summary>
    InvalidRequest
}

/// <summary>
/// The result of a document request, carrying the outcome so a caller can tell the failures
/// apart without parsing a message.
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

    internal static DocumentResult<T> Unavailable(string message) =>
        new(DocumentOutcome.Unavailable, default, message);

    internal static DocumentResult<T> NotReady(string message) =>
        new(DocumentOutcome.NotReady, default, message);

    internal static DocumentResult<T> InvalidRequest(string message) =>
        new(DocumentOutcome.InvalidRequest, default, message);
}

/// <summary>
/// Reads the coding standards this server serves.
/// </summary>
/// <remarks>
/// Every member is asynchronous because every member may find the cached catalog past its
/// lifetime and reload it before answering. Inside the cache window nothing touches the
/// network. When no catalog has ever loaded, every member reports
/// <see cref="DocumentOutcome.NotReady"/> rather than an empty result - "there are no
/// documents" and "I cannot reach GitHub" mean different things to a caller.
/// </remarks>
public interface IDocumentService
{
    /// <summary>Whether a catalog has loaded and the service can answer.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Lists every catalogued document with its full metadata, including <c>Status</c>,
    /// ordered by category then id.
    /// </summary>
    Task<DocumentResult<IReadOnlyList<DocumentSummary>>> GetIndexAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists every catalogued document as a listing entry - five fields, no body, no status -
    /// ordered by category then id.
    /// </summary>
    Task<DocumentResult<IReadOnlyList<DocumentListEntry>>> GetListingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a document and its body by exact, case-sensitive id, fetching the body if no
    /// unexpired cached copy is held.
    /// </summary>
    Task<DocumentResult<Document>> GetDocumentAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Searches document metadata for a keyword, case-insensitively.
    /// </summary>
    /// <remarks>
    /// Metadata only - title, description, and tags. Bodies are fetched per document and are
    /// not resident, so searching them would mean fetching the whole corpus on the first
    /// search. See the <c>docs-serve-document-by-id</c> design.
    /// </remarks>
    Task<DocumentResult<IReadOnlyList<DocumentSummary>>> SearchAsync(string keyword, CancellationToken cancellationToken);

    /// <summary>
    /// Selects the documents carrying a tag, exactly if any do and approximately if none do.
    /// </summary>
    /// <remarks>
    /// The tag is trimmed and lowercased before matching. Documents whose tag equals it are
    /// returned; only when none does are documents whose tag merely contains it returned,
    /// and only when the tag is at least two characters. Reads catalog metadata alone - no
    /// GitHub request, no document body. Entries are ordered by category then id.
    /// </remarks>
    Task<DocumentResult<TagSelection>> FindByTagAsync(string tag, CancellationToken cancellationToken);
}
