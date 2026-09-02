using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// Downloads the content archive from GitHub over HTTPS.
/// </summary>
public sealed class GitHubContentArchiveSource : IContentArchiveSource
{
    /// <summary>The name of the <see cref="HttpClient"/> this source resolves.</summary>
    public const string HttpClientName = "github-content";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<GitHubContentOptions> _options;
    private readonly ILogger<GitHubContentArchiveSource> _logger;

    /// <summary>Creates the source over a named <see cref="HttpClient"/>.</summary>
    public GitHubContentArchiveSource(
        HttpClient httpClient,
        IOptionsMonitor<GitHubContentOptions> options,
        ILogger<GitHubContentArchiveSource> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenArchiveAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var uri = options.ArchiveUri;

        // Only the URI is logged. The token, if any, must never reach the logs.
        _logger.LogInformation(
            "Downloading content archive for {Owner}/{Repository}@{Ref}.",
            options.Owner,
            options.Repository,
            options.Ref);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            throw new ContentUnavailableException(
                $"Could not reach GitHub to download {options.Owner}/{options.Repository}@{options.Ref}.",
                exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();

            // A 404 on the archive almost always means a misconfigured ref rather than an
            // outage, so say so - it is the difference between waiting and fixing config.
            var hint = status == HttpStatusCode.NotFound
                ? $" Check that the ref '{options.Ref}' exists and the repository is public or the access token has access."
                : string.Empty;

            throw new ContentUnavailableException(
                $"GitHub returned {(int)status} {status} for {options.Owner}/{options.Repository}@{options.Ref}.{hint}");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new ResponseStream(stream, response);
    }

    /// <summary>
    /// Keeps the response alive for as long as the caller reads the stream, so disposing the
    /// stream releases the connection.
    /// </summary>
    private sealed class ResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
