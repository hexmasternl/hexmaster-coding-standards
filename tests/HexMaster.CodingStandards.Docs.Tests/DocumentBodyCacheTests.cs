using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

public class DocumentBodyCacheTests
{
    private const string Path = "docs/ADR/0001-a-decision.md";

    [Fact]
    public async Task FetchesOnAMissAndReturnsTheBody()
    {
        var (cache, client, _) = Cache(github => github.WithBody(Path, "# A decision"));

        var fetch = await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        fetch.Result.Content.ShouldBe("# A decision");
        client.BodyCallsFor(Path).ShouldBe(1);
    }

    [Fact]
    public async Task ServesFromCacheInsideTheLifetimeWithNoFetch()
    {
        var (cache, client, time) = Cache(github => github.WithBody(Path, "# A decision"));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(29));
        var second = await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        second.Result.Content.ShouldBe("# A decision");
        client.BodyCallsFor(Path).ShouldBe(1);
    }

    [Fact]
    public async Task RefetchesOnceTheLifetimeHasElapsed()
    {
        var (cache, client, time) = Cache(github => github.WithBody(Path, "# A decision"));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(31));
        await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        client.BodyCallsFor(Path).ShouldBe(2);
    }

    [Fact]
    public async Task ReadingDoesNotExtendTheLifetime()
    {
        // Expiry is absolute from the fetch instant, so a document read every few minutes
        // still expires on schedule rather than keeping itself alive indefinitely.
        var (cache, client, time) = Cache(github => github.WithBody(Path, "# A decision"));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        for (var minute = 0; minute < 5; minute++)
        {
            time.Advance(TimeSpan.FromMinutes(5));
            await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        }

        client.BodyCallsFor(Path).ShouldBe(1);

        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        client.BodyCallsFor(Path).ShouldBe(2);
    }

    [Fact]
    public async Task HonoursAConfiguredNonDefaultLifetime()
    {
        var (cache, client, time) = Cache(
            github => github.WithBody(Path, "# A decision"),
            options => options.BodyCacheLifetime = TimeSpan.FromMinutes(5));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromMinutes(4));
        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        client.BodyCallsFor(Path).ShouldBe(1);

        time.Advance(TimeSpan.FromMinutes(2));
        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        client.BodyCallsFor(Path).ShouldBe(2);
    }

    [Fact]
    public async Task AConcurrentBurstProducesExactlyOneFetch()
    {
        var (cache, client, _) = Cache(github => github
            .WithBody(Path, "# A decision")
            .GateBodies());

        var requests = Enumerable.Range(0, 20)
            .Select(_ => cache.GetAsync(Path, TestContext.Current.CancellationToken))
            .ToArray();

        client.ReleaseBodies();
        var results = await Task.WhenAll(requests);

        client.BodyCallsFor(Path).ShouldBe(1);
        results.Length.ShouldBe(20);
        results.ShouldAllBe(fetch => fetch.Result.Content == "# A decision");
    }

    [Fact]
    public async Task AFailedFetchIsNotRetained()
    {
        var (cache, client, _) = Cache(github => github.WithBodyFailure(Path));

        var first = await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        first.Result.IsSuccess.ShouldBeFalse();
        cache.Count.ShouldBe(0);

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);

        // Caching a transient failure for the lifetime would turn a blip into a half-hour
        // outage for that document.
        client.BodyCallsFor(Path).ShouldBe(2);
    }

    [Fact]
    public async Task EveryWaiterOnASharedFailureObservesIt()
    {
        var (cache, client, _) = Cache(github => github
            .WithBodyFailure(Path, ContentFetchStatus.RateLimited, "rate limited")
            .GateBodies());

        var requests = Enumerable.Range(0, 10)
            .Select(_ => cache.GetAsync(Path, TestContext.Current.CancellationToken))
            .ToArray();

        client.ReleaseBodies();
        var results = await Task.WhenAll(requests);

        client.BodyCallsFor(Path).ShouldBe(1);
        results.ShouldAllBe(fetch => fetch.Result.Status == ContentFetchStatus.RateLimited);
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ARecoveredFetchSucceedsOnRetry()
    {
        var (cache, _, _) = Cache(github => github.WithBodyFailure(Path));

        (await cache.GetAsync(Path, TestContext.Current.CancellationToken)).Result.IsSuccess.ShouldBeFalse();

        // The underlying cause is resolved.
        _github!.WithBody(Path, "# A decision");

        var second = await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        second.Result.Content.ShouldBe("# A decision");
    }

    [Fact]
    public async Task SweepDiscardsExpiredEntriesOnly()
    {
        const string Other = "docs/Designs/a-design.md";

        var (cache, _, time) = Cache(github => github
            .WithBody(Path, "# A decision")
            .WithBody(Other, "# A design"));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(20));
        await cache.GetAsync(Other, TestContext.Current.CancellationToken);

        cache.Count.ShouldBe(2);

        // Now the first is 31 minutes old and the second only 11.
        time.Advance(TimeSpan.FromMinutes(11));
        cache.SweepExpired().ShouldBe(1);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SweepKeepsEverythingInsideTheLifetime()
    {
        var (cache, _, time) = Cache(github => github.WithBody(Path, "# A decision"));

        await cache.GetAsync(Path, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(10));

        cache.SweepExpired().ShouldBe(0);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task KeyingByPathMeansARepointedEntryMissesTheCache()
    {
        const string NewPath = "docs/ADR/0001-a-renamed-decision.md";

        var (cache, client, _) = Cache(github => github
            .WithBody(Path, "# The old file")
            .WithBody(NewPath, "# The new file"));

        (await cache.GetAsync(Path, TestContext.Current.CancellationToken)).Result.Content
            .ShouldBe("# The old file");

        (await cache.GetAsync(NewPath, TestContext.Current.CancellationToken)).Result.Content
            .ShouldBe("# The new file");

        client.BodyCallsFor(NewPath).ShouldBe(1);
    }

    private FakeGitHubContentClient? _github;

    private (DocumentBodyCache Cache, FakeGitHubContentClient Client, FakeTimeProvider Time) Cache(
        Action<FakeGitHubContentClient> configureClient,
        Action<GitHubContentOptions>? configureOptions = null)
    {
        _github = new FakeGitHubContentClient();
        configureClient(_github);

        var options = new GitHubContentOptions();
        configureOptions?.Invoke(options);

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null));

        return (new DocumentBodyCache(_github, new StaticOptionsMonitor<GitHubContentOptions>(options), time),
            _github,
            time);
    }
}
