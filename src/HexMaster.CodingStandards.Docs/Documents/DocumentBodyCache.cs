using System.Collections.Concurrent;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.Documents;

/// <summary>The outcome of a body lookup, with the instant the body was fetched.</summary>
/// <param name="Result">Whether content was obtained, and if not, why.</param>
/// <param name="FetchedAt">When the body was fetched, which is when its lifetime started.</param>
public sealed record BodyFetch(ContentFetchResult Result, DateTimeOffset FetchedAt);

/// <summary>
/// Caches document bodies individually, keyed by the content path they were fetched from.
/// </summary>
/// <remarks>
/// Three properties are load-bearing and each is tested:
///
/// <list type="bullet">
/// <item>
/// Expiry is <b>absolute from the fetch instant</b>, not sliding. A popular document must
/// not be able to keep itself alive indefinitely, because bounded staleness is what is being
/// promised.
/// </item>
/// <item>
/// <b>Single-flight per key.</b> Concurrent requests for one uncached document share a
/// single fetch, so a cold replica hit by a burst does not multiply its own rate-limit
/// consumption for nothing.
/// </item>
/// <item>
/// <b>Failures are never retained.</b> Caching a transient 5xx or a rate-limit refusal for
/// half an hour would turn a blip into an outage for that document.
/// </item>
/// </list>
///
/// Keying by path rather than by id means a catalog edit that repoints an id at another file
/// misses the cache immediately, instead of serving the old file until expiry.
/// </remarks>
public sealed class DocumentBodyCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<BodyFetch>>> _entries = new(StringComparer.Ordinal);
    private readonly IGitHubContentClient _client;
    private readonly IOptionsMonitor<GitHubContentOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the cache.</summary>
    public DocumentBodyCache(
        IGitHubContentClient client,
        IOptionsMonitor<GitHubContentOptions> options,
        TimeProvider timeProvider)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>How many bodies are currently held, expired ones included.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns a document body, from cache when it is present and unexpired, otherwise by
    /// fetching it.
    /// </summary>
    public async Task<BodyFetch> GetAsync(string contentPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = _entries.GetOrAdd(
                contentPath,
                path => new Lazy<Task<BodyFetch>>(
                    () => FetchAsync(path),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            var fetched = await entry.Value.ConfigureAwait(false);

            if (!fetched.Result.IsSuccess)
            {
                // Evict this exact entry, so the next caller retries rather than replaying a
                // stale failure. Every request already awaiting it still observes this one.
                Remove(contentPath, entry);
                return fetched;
            }

            if (!IsExpired(fetched.FetchedAt))
            {
                return fetched;
            }

            // Expired: drop this entry and loop, so the next iteration starts a fresh fetch.
            // Removing the specific entry rather than the key avoids discarding a refill
            // another thread has already begun.
            Remove(contentPath, entry);
        }
    }

    /// <summary>
    /// Discards expired bodies. Called when a catalog refresh completes, so an idle replica
    /// does not hold content it will never serve again.
    /// </summary>
    public int SweepExpired()
    {
        var swept = 0;

        foreach (var (path, entry) in _entries.ToArray())
        {
            // An in-flight fetch has nothing to sweep, and touching it would defeat
            // single-flight.
            if (!entry.IsValueCreated || !entry.Value.IsCompletedSuccessfully)
            {
                continue;
            }

            var fetched = entry.Value.Result;
            if (!fetched.Result.IsSuccess || IsExpired(fetched.FetchedAt))
            {
                if (Remove(path, entry))
                {
                    swept++;
                }
            }
        }

        return swept;
    }

    private async Task<BodyFetch> FetchAsync(string contentPath)
    {
        // Deliberately not the caller's token: the fetch is shared, so letting the first
        // caller's cancellation abort it would fail every other waiter too. Requests are
        // bounded by the HttpClient timeout instead.
        var result = await _client.GetContentAsync(contentPath, CancellationToken.None)
            .ConfigureAwait(false);

        return new BodyFetch(result, _timeProvider.GetUtcNow());
    }

    private bool IsExpired(DateTimeOffset fetchedAt) =>
        _timeProvider.GetUtcNow() - fetchedAt >= _options.CurrentValue.BodyCacheLifetime;

    private bool Remove(string contentPath, Lazy<Task<BodyFetch>> entry) =>
        _entries.TryRemove(new KeyValuePair<string, Lazy<Task<BodyFetch>>>(contentPath, entry));
}
