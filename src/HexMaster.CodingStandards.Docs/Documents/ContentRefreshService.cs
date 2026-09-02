using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Loads the content set on startup and refreshes it on the configured interval.
/// </summary>
/// <remarks>
/// The failure behaviour is the point of this type. Once content has loaded, a failed
/// refresh logs and leaves the cached set in place, so a GitHub outage costs freshness
/// rather than availability. A failure before anything has loaded leaves the cache empty,
/// which is what makes the replica report unhealthy and be replaced.
/// </remarks>
public sealed class ContentRefreshService : BackgroundService
{
    private readonly IContentArchiveSource _archiveSource;
    private readonly DocumentSetCache _cache;
    private readonly IOptionsMonitor<GitHubContentOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContentRefreshService> _logger;

    /// <summary>Creates the refresh service.</summary>
    public ContentRefreshService(
        IContentArchiveSource archiveSource,
        DocumentSetCache cache,
        IOptionsMonitor<GitHubContentOptions> options,
        TimeProvider timeProvider,
        ILogger<ContentRefreshService> logger)
    {
        _archiveSource = archiveSource;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Downloads and loads the content set once. Returns whether the cache now holds newly
    /// loaded content.
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var archive = await _archiveSource.OpenArchiveAsync(cancellationToken)
                .ConfigureAwait(false);

            var extracted = await ContentArchiveExtractor
                .ExtractAsync(archive, _logger, cancellationToken)
                .ConfigureAwait(false);

            var set = DocumentSet.FromExtractedContent(extracted, _logger, _timeProvider);

            _cache.Replace(set);
            return true;
        }
        catch (Exception exception) when (exception is ContentUnavailableException or CatalogFormatException)
        {
            if (_cache.HasContent)
            {
                _logger.LogWarning(
                    exception,
                    "Content refresh failed; continuing to serve the content loaded at {LoadedAt:o}.",
                    _cache.Current!.LoadedAt);
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Content refresh failed and nothing is cached; the server cannot serve documents yet.");
            }

            return false;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken).ConfigureAwait(false);

        // A periodic timer rather than a delay loop: the interval is honoured from tick to
        // tick, so a slow refresh does not push every later refresh further out.
        using var timer = new PeriodicTimer(_options.CurrentValue.RefreshInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
