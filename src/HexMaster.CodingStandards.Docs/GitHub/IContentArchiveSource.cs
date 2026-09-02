namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// Supplies the raw content archive for the configured ref.
/// </summary>
/// <remarks>
/// The download sits behind this interface so tests exercise loading, extraction, retrieval,
/// and search against fixture archives with no network. It is the only seam the document
/// service needs to be substitutable.
/// </remarks>
public interface IContentArchiveSource
{
    /// <summary>
    /// Downloads the repository archive as a gzipped tarball stream. The caller disposes it.
    /// </summary>
    /// <exception cref="ContentUnavailableException">The archive could not be fetched.</exception>
    Task<Stream> OpenArchiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when the content archive cannot be fetched from GitHub. Distinct from a parse
/// failure: this one is upstream and usually transient, which is why a refresh that hits it
/// keeps serving the previously loaded content.
/// </summary>
public sealed class ContentUnavailableException : Exception
{
    /// <summary>Creates the exception with a message and the underlying transport failure.</summary>
    public ContentUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
