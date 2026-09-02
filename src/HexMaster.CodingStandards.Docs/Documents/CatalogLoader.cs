using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>
/// Loads the catalog once at startup, then reloads it on the first read that finds the
/// cached copy past its lifetime.
/// </summary>
/// <remarks>
/// Two things here are load-bearing.
///
/// <para>
/// <b>The startup load.</b> Expiry is evaluated on read, not on a timer, so an idle replica
/// issues no requests. But the readiness probe hits <c>/health</c>, and <c>/health</c> is
/// unhealthy until a catalog has loaded - so a replica that only loaded on demand would
/// never be ready, never be sent a request, and never load. The eager first load costs the
/// same fetch the first caller would have paid, moved earlier.
/// </para>
///
/// <para>
/// <b>The failure behaviour.</b> Once a catalog has loaded, a failed reload logs and leaves
/// the cached catalog in place, so a GitHub outage costs freshness rather than availability.
/// A failure before anything has loaded leaves the cache empty, which is what makes the
/// replica report unhealthy and be replaced.
/// </para>
///
/// Only the catalog is loaded here. Bodies are fetched per document on demand; this type
/// merely sweeps the expired ones after a successful load.
/// </remarks>
public sealed class CatalogLoader : BackgroundService
{
    private readonly IGitHubContentClient _client;
    private readonly DocumentSetCache _cache;
    private readonly DocumentBodyCache _bodyCache;
    private readonly IOptionsMonitor<GitHubContentOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogLoader> _logger;

    private readonly Lock _sync = new();

    // The load currently running, if any. Callers arriving on an expired cache await this
    // rather than starting their own, so a burst produces one fetch and not one per caller -
    // which matters most during an outage, where the alternative is every caller serially
    // paying the request timeout.
    private Task? _inFlight;

    /// <summary>Creates the loader.</summary>
    public CatalogLoader(
        IGitHubContentClient client,
        DocumentSetCache cache,
        DocumentBodyCache bodyCache,
        IOptionsMonitor<GitHubContentOptions> options,
        TimeProvider timeProvider,
        ILogger<CatalogLoader> logger)
    {
        _client = client;
        _cache = cache;
        _bodyCache = bodyCache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Ensures a catalog is available and no older than its configured lifetime, loading it
    /// if not. Returns without loading when the cached catalog is still current.
    /// </summary>
    /// <remarks>
    /// Never throws for an upstream failure: a failed reload over a cached catalog is a
    /// freshness problem, and the caller finds out by reading a catalog that is simply older
    /// than it hoped. A failure with nothing cached leaves the cache empty, and the caller
    /// reports not-ready.
    /// </remarks>
    public Task EnsureCurrentAsync(CancellationToken cancellationToken)
    {
        var lifetime = _options.CurrentValue.CatalogCacheLifetime;

        if (!_cache.IsExpired(_timeProvider.GetUtcNow(), lifetime))
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            // Re-checked under the lock: the load we would have started may have completed
            // between the check above and here.
            if (!_cache.IsExpired(_timeProvider.GetUtcNow(), lifetime))
            {
                return Task.CompletedTask;
            }

            if (_inFlight is not null)
            {
                return _inFlight;
            }

            // Whoever gets here first starts the load; everyone else awaits that same task,
            // so a burst costs one fetch whether it succeeds or fails. Once it finishes the
            // field is cleared, and a later caller that still finds the cache expired - which
            // is what a failed load leaves behind - starts a fresh one.
            var load = LoadAndClearAsync();

            // A load that finished synchronously has already run its cleanup - which, being
            // reentrant on this lock, cleared a field we had not assigned yet. Storing it now
            // would park a completed task here forever and suppress every later reload.
            _inFlight = load.IsCompleted ? null : load;

            return load;
        }

        // Deliberately not the caller's token. The load is shared, so honouring one caller's
        // cancellation would abort a fetch the others are waiting on; the HTTP client's own
        // timeout is what bounds it.
        async Task LoadAndClearAsync()
        {
            try
            {
                await LoadAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight = null;
                }
            }
        }
    }

    /// <summary>
    /// Fetches and loads the catalog once, regardless of the cache window. Returns whether
    /// the cache now holds a newly loaded catalog.
    /// </summary>
    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalogJson = await _client.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var set = DocumentSet.FromCatalogJson(catalogJson, _logger, _timeProvider);

            _cache.Replace(set);

            var swept = _bodyCache.SweepExpired();
            if (swept > 0)
            {
                _logger.LogDebug("Swept {SweptCount} expired document body/bodies.", swept);
            }

            return true;
        }
        catch (Exception exception) when (exception is ContentUnavailableException or CatalogFormatException)
        {
            if (_cache.HasContent)
            {
                _logger.LogWarning(
                    exception,
                    "Catalog load failed; continuing to serve the catalog loaded at {LoadedAt:o}.",
                    _cache.Current!.LoadedAt);
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Catalog load failed and nothing is cached; the server cannot serve documents yet.");
            }

            return false;
        }
    }

    /// <summary>
    /// Loads the catalog once, shortly after the host starts.
    /// </summary>
    /// <remarks>
    /// A background service rather than work in <c>StartAsync</c> so the host begins
    /// listening immediately: the probe's initial delay and failure threshold give this load
    /// ample room to finish, and a slow GitHub delays readiness rather than startup itself.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await LoadAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down before the first load finished.
        }
    }
}
