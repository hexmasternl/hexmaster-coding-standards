using System.Collections.Concurrent;
using HexMaster.CodingStandards.Docs.GitHub;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// Stands in for GitHub. Catalog outcomes are queued so a test can script "succeeds, then
/// fails, then succeeds"; body outcomes are per path so one document can fail while others
/// succeed.
/// </summary>
internal sealed class FakeGitHubContentClient : IGitHubContentClient
{
    private readonly Queue<Func<string>> _catalogOutcomes = new();
    private readonly ConcurrentDictionary<string, Func<ContentFetchResult>> _bodies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _bodyCalls = new(StringComparer.Ordinal);
    private readonly Lock _catalogLock = new();
    private TaskCompletionSource? _bodyGate;
    private TaskCompletionSource? _catalogGate;
    private int _catalogCalls;
    private int _catalogInFlight;
    private int _maxCatalogInFlight;

    /// <summary>How many times the catalog was requested.</summary>
    public int CatalogCalls => Volatile.Read(ref _catalogCalls);

    /// <summary>
    /// The most catalog fetches that were ever in flight at the same moment.
    /// </summary>
    /// <remarks>
    /// This is what single-flight actually means, and unlike a total it does not depend on
    /// how many callers happened to arrive before a gate was released.
    /// </remarks>
    public int MaxConcurrentCatalogCalls => Volatile.Read(ref _maxCatalogInFlight);

    /// <summary>Total body requests across every path.</summary>
    public int TotalBodyCalls => _bodyCalls.Values.Sum();

    /// <summary>How many times a given path's body was requested.</summary>
    public int BodyCallsFor(string contentPath) =>
        _bodyCalls.TryGetValue(contentPath, out var count) ? count : 0;

    /// <summary>Queues a successful catalog response. The last queued outcome repeats.</summary>
    public FakeGitHubContentClient WithCatalog(string json)
    {
        _catalogOutcomes.Enqueue(() => json);
        return this;
    }

    /// <summary>Queues a failed catalog fetch.</summary>
    public FakeGitHubContentClient WithCatalogFailure(string message = "GitHub is unreachable.")
    {
        _catalogOutcomes.Enqueue(() => throw new ContentUnavailableException(message));
        return this;
    }

    /// <summary>Sets a path's body to be returned successfully.</summary>
    public FakeGitHubContentClient WithBody(string contentPath, string markdown)
    {
        _bodies[contentPath] = () => new ContentFetchResult(ContentFetchStatus.Success, markdown);
        return this;
    }

    /// <summary>Sets a path's body to fail with the given status.</summary>
    public FakeGitHubContentClient WithBodyFailure(
        string contentPath,
        ContentFetchStatus status = ContentFetchStatus.Failed,
        string reason = "the fake client was told to fail")
    {
        _bodies[contentPath] = () => new ContentFetchResult(status, null, reason);
        return this;
    }

    /// <summary>
    /// Holds every body fetch until <see cref="ReleaseBodies"/> is called, so a test can get
    /// several requests genuinely in flight at once.
    /// </summary>
    public FakeGitHubContentClient GateBodies()
    {
        _bodyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>Releases gated body fetches.</summary>
    public void ReleaseBodies() => _bodyGate?.TrySetResult();

    /// <summary>
    /// Holds every catalog fetch until <see cref="ReleaseCatalog"/> is called, so a test can
    /// get several catalog requests genuinely in flight at once.
    /// </summary>
    public FakeGitHubContentClient GateCatalog()
    {
        _catalogGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    /// <summary>Releases gated catalog fetches.</summary>
    public void ReleaseCatalog() => _catalogGate?.TrySetResult();

    /// <inheritdoc />
    public async Task<string> GetCatalogAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _catalogCalls);

        var inFlight = Interlocked.Increment(ref _catalogInFlight);

        int observed;
        while (inFlight > (observed = Volatile.Read(ref _maxCatalogInFlight)))
        {
            Interlocked.CompareExchange(ref _maxCatalogInFlight, inFlight, observed);
        }

        try
        {
            if (_catalogGate is not null)
            {
                await _catalogGate.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _catalogInFlight);
        }

        Func<string> outcome;

        // The queue is not thread-safe and concurrency tests drive this from several tasks.
        lock (_catalogLock)
        {
            if (_catalogOutcomes.Count == 0)
            {
                throw new ContentUnavailableException("No catalog outcome was queued for this call.");
            }

            outcome = _catalogOutcomes.Count == 1 ? _catalogOutcomes.Peek() : _catalogOutcomes.Dequeue();
        }

        return outcome();
    }

    /// <inheritdoc />
    public async Task<ContentFetchResult> GetContentAsync(string contentPath, CancellationToken cancellationToken)
    {
        _bodyCalls.AddOrUpdate(contentPath, 1, (_, count) => count + 1);

        if (_bodyGate is not null)
        {
            await _bodyGate.Task.ConfigureAwait(false);
        }

        return _bodies.TryGetValue(contentPath, out var body)
            ? body()
            : new ContentFetchResult(ContentFetchStatus.NotFound, null, $"no fake body for '{contentPath}'");
    }
}
