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
    private TaskCompletionSource? _bodyGate;

    /// <summary>How many times the catalog was requested.</summary>
    public int CatalogCalls { get; private set; }

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

    /// <inheritdoc />
    public Task<string> GetCatalogAsync(CancellationToken cancellationToken)
    {
        CatalogCalls++;

        if (_catalogOutcomes.Count == 0)
        {
            throw new ContentUnavailableException("No catalog outcome was queued for this call.");
        }

        var outcome = _catalogOutcomes.Count == 1 ? _catalogOutcomes.Peek() : _catalogOutcomes.Dequeue();
        return Task.FromResult(outcome());
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
