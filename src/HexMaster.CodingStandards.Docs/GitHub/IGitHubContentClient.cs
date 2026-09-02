namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>Why a body fetch did not return content.</summary>
public enum ContentFetchStatus
{
    /// <summary>The content was fetched.</summary>
    Success,

    /// <summary>GitHub has no file at that path and ref.</summary>
    NotFound,

    /// <summary>
    /// GitHub refused the request on rate-limit grounds. Kept distinct from a general
    /// failure because the fix is a token or patience, not a code change.
    /// </summary>
    RateLimited,

    /// <summary>The request failed for any other reason, including a transport failure or timeout.</summary>
    Failed
}

/// <summary>
/// The outcome of fetching one file's content.
/// </summary>
/// <param name="Status">Whether the fetch succeeded, and if not, why.</param>
/// <param name="Content">The file's text, present only on success.</param>
/// <param name="Reason">
/// A short, caller-safe description of a failure. Never carries the access token, request
/// headers, or a stack trace.
/// </param>
public sealed record ContentFetchResult(ContentFetchStatus Status, string? Content, string? Reason = null)
{
    /// <summary>Whether content was fetched.</summary>
    public bool IsSuccess => Status == ContentFetchStatus.Success;

    internal static ContentFetchResult Success(string content) => new(ContentFetchStatus.Success, content);

    internal static ContentFetchResult NotFound(string reason) => new(ContentFetchStatus.NotFound, null, reason);

    internal static ContentFetchResult RateLimited(string reason) => new(ContentFetchStatus.RateLimited, null, reason);

    internal static ContentFetchResult Failed(string reason) => new(ContentFetchStatus.Failed, null, reason);
}

/// <summary>
/// Reads files from the content repository on GitHub.
/// </summary>
/// <remarks>
/// The only network seam the document layer has, so tests substitute it and run offline.
/// The two members fail differently on purpose: a catalog that cannot be fetched throws,
/// because a refresh either replaces the catalog or leaves the previous one alone, while a
/// body that cannot be fetched returns a status, because it degrades one document and
/// nothing else.
/// </remarks>
public interface IGitHubContentClient
{
    /// <summary>
    /// Fetches <c>docs/index.json</c> in full at the configured ref.
    /// </summary>
    /// <exception cref="ContentUnavailableException">The catalog could not be fetched.</exception>
    Task<string> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one document's markdown at the configured ref. The path must already have
    /// passed <see cref="Catalog.ContentPath.IsValid"/>.
    /// </summary>
    Task<ContentFetchResult> GetContentAsync(string contentPath, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when the catalog cannot be fetched from GitHub. Distinct from a parse failure:
/// this one is upstream and usually transient, which is why a refresh that hits it keeps
/// serving the previously loaded catalog.
/// </summary>
public sealed class ContentUnavailableException : Exception
{
    /// <summary>Creates the exception with a message and the underlying transport failure.</summary>
    public ContentUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
