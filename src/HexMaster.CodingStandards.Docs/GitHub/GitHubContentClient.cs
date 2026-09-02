using System.Net;
using System.Net.Http.Headers;
using HexMaster.CodingStandards.Docs.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// Reads files from the content repository through the GitHub Contents API.
/// </summary>
public sealed class GitHubContentClient : IGitHubContentClient
{
    /// <summary>The name of the <see cref="HttpClient"/> this client resolves.</summary>
    public const string HttpClientName = "github-content";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<GitHubContentOptions> _options;
    private readonly ILogger<GitHubContentClient> _logger;

    /// <summary>Creates the client over a named <see cref="HttpClient"/>.</summary>
    public GitHubContentClient(
        HttpClient httpClient,
        IOptionsMonitor<GitHubContentOptions> options,
        ILogger<GitHubContentClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        _logger.LogInformation(
            "Fetching the document catalog for {Owner}/{Repository}@{Ref}.",
            options.Owner,
            options.Repository,
            options.Ref);

        var result = await GetContentAsync(GitHubContentOptions.CatalogPath, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return result.Content!;
        }

        // A catalog failure has to throw: a refresh either replaces the catalog wholesale or
        // leaves the previous one untouched, and there is no partial catalog worth serving.
        throw new ContentUnavailableException(
            $"Could not fetch {GitHubContentOptions.CatalogPath} for {options.Owner}/{options.Repository}@{options.Ref}: {result.Reason}");
    }

    /// <inheritdoc />
    public async Task<ContentFetchResult> GetContentAsync(string contentPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        var options = _options.CurrentValue;
        var uri = options.ContentUri(ContentPath.Encode(contentPath));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // The raw media type returns the file's bytes rather than a JSON envelope with
        // base64 content, so there is nothing to decode.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return ContentFetchResult.Success(content);
            }

            return await MapFailureAsync(response, contentPath, options).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            // The exception is logged in full; the returned reason stays caller-safe, with no
            // stack trace, no request headers, and no token.
            _logger.LogWarning(
                exception,
                "Request for '{ContentPath}' at {Owner}/{Repository}@{Ref} failed.",
                contentPath,
                options.Owner,
                options.Repository,
                options.Ref);

            return ContentFetchResult.Failed(
                exception is TaskCanceledException
                    ? "the request to GitHub timed out"
                    : "GitHub could not be reached");
        }
    }

    private async Task<ContentFetchResult> MapFailureAsync(
        HttpResponseMessage response,
        string contentPath,
        GitHubContentOptions options)
    {
        var status = response.StatusCode;

        if (status == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "GitHub has no file at '{ContentPath}' for {Owner}/{Repository}@{Ref}.",
                contentPath,
                options.Owner,
                options.Repository,
                options.Ref);

            return ContentFetchResult.NotFound(
                $"GitHub has no file at '{contentPath}' at ref '{options.Ref}'");
        }

        if (IsRateLimited(response, out var retryHint))
        {
            _logger.LogWarning(
                "GitHub rate-limited the request for '{ContentPath}'. {RetryHint}",
                contentPath,
                retryHint);

            return ContentFetchResult.RateLimited(
                $"GitHub rate-limited the request. {retryHint}");
        }

        // Read a little of the body for the log only - never into the returned reason.
        var detail = await ReadShortDetailAsync(response).ConfigureAwait(false);
        _logger.LogWarning(
            "GitHub returned {StatusCode} for '{ContentPath}'. {Detail}",
            (int)status,
            contentPath,
            detail);

        return ContentFetchResult.Failed($"GitHub returned {(int)status} {status}");
    }

    /// <summary>
    /// Distinguishes a rate-limit refusal from an ordinary authorization failure. GitHub uses
    /// 403 for both, so the headers are what separate them.
    /// </summary>
    private static bool IsRateLimited(HttpResponseMessage response, out string retryHint)
    {
        retryHint = string.Empty;

        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return false;
        }

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining)
            && remaining.FirstOrDefault() == "0")
        {
            retryHint = response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
                        && long.TryParse(reset.FirstOrDefault(), out var resetAt)
                ? $"The limit resets at {DateTimeOffset.FromUnixTimeSeconds(resetAt):u}."
                : "Configure an access token to raise the limit.";
            return true;
        }

        if (response.Headers.TryGetValues("retry-after", out var retryAfter))
        {
            retryHint = $"Retry after {retryAfter.FirstOrDefault()} second(s).";
            return true;
        }

        return response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static async Task<string> ReadShortDetailAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return body.Length > 200 ? body[..200] : body;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ObjectDisposedException)
        {
            return "(no response body)";
        }
    }
}
