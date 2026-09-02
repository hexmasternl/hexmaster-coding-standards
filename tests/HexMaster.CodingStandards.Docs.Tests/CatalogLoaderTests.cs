using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// The catalog's freshness model: loaded once at startup, then reloaded by the first read
/// that finds it past its lifetime. A fake clock is what makes "still cached at 29 minutes,
/// refetched at 31" an ordinary test rather than a wait.
/// </summary>
public class CatalogLoaderTests
{
    private const string OneDocument = """
        { "documents": [ {
            "id": "a-decision", "title": "A decision",
            "description": "Decides a thing.", "category": "ADR",
            "status": "accepted", "tags": [], "path": "docs/ADR/a-decision.md" } ] }
        """;

    private const string TwoDocuments = """
        { "documents": [
          { "id": "a-decision", "title": "A decision",
            "description": "Decides a thing.", "category": "ADR",
            "status": "accepted", "tags": [], "path": "docs/ADR/a-decision.md" },
          { "id": "b-decision", "title": "B decision",
            "description": "Decides another thing.", "category": "ADR",
            "status": "accepted", "tags": [], "path": "docs/ADR/b-decision.md" } ] }
        """;

    [Fact]
    public async Task LoadsTheCatalogAtStartupBeforeAnyRequestArrives()
    {
        // Without this the deployment deadlocks: the readiness probe reads /health, /health
        // is unhealthy until a catalog has loaded, and an unready replica is sent no request
        // that could trigger a lazy load.
        var context = new LoaderContext().WithCatalog(OneDocument);

        await context.StartAsync();

        context.Cache.HasContent.ShouldBeTrue();
        context.Github.CatalogCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ServesFromCacheInsideTheWindowWithoutFetching()
    {
        var context = new LoaderContext().WithCatalog(OneDocument);
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(29));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);

        context.Github.CatalogCalls.ShouldBe(1);
    }

    [Fact]
    public async Task RefetchesOnTheFirstRequestAfterExpiry()
    {
        var context = new LoaderContext().WithCatalog(OneDocument).WithCatalog(TwoDocuments);
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(31));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);

        context.Github.CatalogCalls.ShouldBe(2);
        context.Cache.Current!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task IssuesNoFetchWhileIdlePastTheWindow()
    {
        // The point of dropping the timer: an idle replica costs GitHub nothing.
        var context = new LoaderContext().WithCatalog(OneDocument);
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromHours(6));

        context.Github.CatalogCalls.ShouldBe(1);
    }

    [Fact]
    public async Task HonoursAConfiguredWindowOtherThanTheDefault()
    {
        var context = new LoaderContext(TimeSpan.FromMinutes(5))
            .WithCatalog(OneDocument)
            .WithCatalog(TwoDocuments);

        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(4));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        context.Github.CatalogCalls.ShouldBe(1);

        context.Time.Advance(TimeSpan.FromMinutes(2));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        context.Github.CatalogCalls.ShouldBe(2);
    }

    [Fact]
    public async Task ConcurrentCallersOnAnExpiredCacheShareOneFetch()
    {
        var context = new LoaderContext().WithCatalog(OneDocument).WithCatalog(TwoDocuments);
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(31));
        context.Github.GateCatalog();

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(
                () => context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken))
            .ToArray();

        context.Github.ReleaseCatalog();
        await Task.WhenAll(callers);

        // One startup load plus exactly one shared reload, however many callers queued: a
        // caller arriving after the shared load succeeds finds the cache fresh and returns
        // without fetching, so the total is not a race.
        context.Github.CatalogCalls.ShouldBe(2);
        context.Github.MaxConcurrentCatalogCalls.ShouldBe(1);
        context.Cache.Current!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConcurrentCallersDoNotEachRetryAFailedFetch()
    {
        var context = new LoaderContext().WithCatalog(OneDocument).WithCatalogFailure();
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(31));
        context.Github.GateCatalog();

        var callers = Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(
                () => context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken))
            .ToArray();

        context.Github.ReleaseCatalog();
        await Task.WhenAll(callers);

        // A failing fetch must not turn a burst into one timed-out request per caller. The
        // total is not asserted: a failed load leaves the cache expired, so a caller arriving
        // after it finishes is entitled to retry - that is the specified behaviour. What must
        // never happen is two fetches running at once.
        context.Github.MaxConcurrentCatalogCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ReplacesTheCachedCatalogAtomically()
    {
        var context = new LoaderContext().WithCatalog(OneDocument).WithCatalog(TwoDocuments);
        await context.StartAsync();

        var before = context.Cache.Current!;

        context.Time.Advance(TimeSpan.FromMinutes(31));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);

        // A reader holding the previous snapshot keeps a consistent view of it.
        before.Count.ShouldBe(1);
        context.Cache.Current!.Count.ShouldBe(2);
        context.Cache.Current.ShouldNotBeSameAs(before);
    }

    [Fact]
    public async Task ServesTheStaleCatalogWhenAReloadFails()
    {
        var context = new LoaderContext().WithCatalog(OneDocument).WithCatalogFailure();
        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(31));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);

        // Freshness degrades; availability does not.
        context.Cache.HasContent.ShouldBeTrue();
        context.Cache.Current!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedFailuresNeverClearTheCache()
    {
        var context = new LoaderContext()
            .WithCatalog(OneDocument)
            .WithCatalogFailure();

        await context.StartAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            context.Time.Advance(TimeSpan.FromMinutes(31));
            await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        }

        context.Cache.HasContent.ShouldBeTrue();
        context.Cache.Current!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RetriesOnTheNextRequestAfterAFailure()
    {
        var context = new LoaderContext()
            .WithCatalog(OneDocument)
            .WithCatalogFailure()
            .WithCatalog(TwoDocuments);

        await context.StartAsync();

        context.Time.Advance(TimeSpan.FromMinutes(31));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        context.Cache.Current!.Count.ShouldBe(1);

        context.Time.Advance(TimeSpan.FromMinutes(31));
        await context.Loader.EnsureCurrentAsync(TestContext.Current.CancellationToken);
        context.Cache.Current!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LeavesTheCacheEmptyWhenTheFirstLoadFails()
    {
        var context = new LoaderContext().WithCatalogFailure();

        await context.StartAsync();

        // Empty, not an empty catalog: the replica reports unhealthy and gets replaced.
        context.Cache.HasContent.ShouldBeFalse();
        context.Cache.Current.ShouldBeNull();
    }

    /// <summary>Assembles the loader over fakes, with the clock and GitHub under test control.</summary>
    private sealed class LoaderContext
    {
        public LoaderContext(TimeSpan? catalogCacheLifetime = null)
        {
            Github = new FakeGitHubContentClient();
            Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null));
            Cache = new DocumentSetCache();

            var options = new StaticOptionsMonitor<GitHubContentOptions>(new GitHubContentOptions
            {
                CatalogCacheLifetime = catalogCacheLifetime ?? TimeSpan.FromMinutes(30)
            });

            Loader = new CatalogLoader(
                Github,
                Cache,
                new DocumentBodyCache(Github, options, Time),
                options,
                Time,
                NullLogger<CatalogLoader>.Instance);
        }

        public FakeGitHubContentClient Github { get; }

        public FakeTimeProvider Time { get; }

        public DocumentSetCache Cache { get; }

        public CatalogLoader Loader { get; }

        public LoaderContext WithCatalog(string json)
        {
            Github.WithCatalog(json);
            return this;
        }

        public LoaderContext WithCatalogFailure()
        {
            Github.WithCatalogFailure();
            return this;
        }

        /// <summary>Runs the startup load to completion, as the host would.</summary>
        public async Task StartAsync()
        {
            await Loader.StartAsync(TestContext.Current.CancellationToken);

            if (Loader.ExecuteTask is not null)
            {
                await Loader.ExecuteTask;
            }
        }
    }
}
