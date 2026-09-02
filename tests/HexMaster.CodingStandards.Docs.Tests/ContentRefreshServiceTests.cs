using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HexMaster.CodingStandards.Docs.Tests;

public class ContentRefreshServiceTests
{
    [Fact]
    public async Task LoadsContentOnFirstRefresh()
    {
        var source = new FakeArchiveSource().Returning(Archive);
        var (refresh, cache) = Refresh(source);

        var loaded = await refresh.RefreshAsync(TestContext.Current.CancellationToken);

        loaded.ShouldBeTrue();
        cache.HasContent.ShouldBeTrue();
        cache.Current!.Catalog.Count.ShouldBe(1);
    }

    [Fact]
    public async Task KeepsServingCachedContentWhenALaterRefreshFails()
    {
        var source = new FakeArchiveSource()
            .Returning(Archive)
            .Failing();

        var (refresh, cache) = Refresh(source);

        await refresh.RefreshAsync(TestContext.Current.CancellationToken);
        var loadedAt = cache.Current!.LoadedAt;

        var secondRefresh = await refresh.RefreshAsync(TestContext.Current.CancellationToken);

        secondRefresh.ShouldBeFalse();
        cache.HasContent.ShouldBeTrue();
        cache.Current!.LoadedAt.ShouldBe(loadedAt);
        cache.Current.Catalog.Count.ShouldBe(1);
    }

    [Fact]
    public async Task LeavesTheCacheEmptyWhenTheFirstLoadFails()
    {
        var (refresh, cache) = Refresh(new FakeArchiveSource().Failing());

        var loaded = await refresh.RefreshAsync(TestContext.Current.CancellationToken);

        loaded.ShouldBeFalse();
        cache.HasContent.ShouldBeFalse();

        // The service must report not-ready rather than an empty document set, so a caller
        // can tell "cannot reach GitHub" from "there are no standards".
        new DocumentService(cache).GetIndex().Outcome.ShouldBe(DocumentOutcome.NotReady);
    }

    [Fact]
    public async Task RepeatedFailuresNeverClearAlreadyLoadedContent()
    {
        var source = new FakeArchiveSource()
            .Returning(Archive)
            .Failing("first failure")
            .Failing("second failure")
            .Failing("third failure");

        var (refresh, cache) = Refresh(source);

        await refresh.RefreshAsync(TestContext.Current.CancellationToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await refresh.RefreshAsync(TestContext.Current.CancellationToken);
        }

        cache.HasContent.ShouldBeTrue();
        cache.Current!.Catalog.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RecoversOnALaterSuccessfulRefresh()
    {
        var source = new FakeArchiveSource()
            .Failing()
            .Returning(Archive);

        var (refresh, cache) = Refresh(source);

        await refresh.RefreshAsync(TestContext.Current.CancellationToken);
        cache.HasContent.ShouldBeFalse();

        await refresh.RefreshAsync(TestContext.Current.CancellationToken);
        cache.HasContent.ShouldBeTrue();
    }

    [Fact]
    public async Task TreatsAnUnparseableCatalogAsAFailedRefresh()
    {
        var source = new FakeArchiveSource()
            .Returning(Archive)
            .Returning(() => new TarGzBuilder()
                .WithFile("docs/index.json", "{ not json")
                .Build());

        var (refresh, cache) = Refresh(source);

        await refresh.RefreshAsync(TestContext.Current.CancellationToken);
        var secondRefresh = await refresh.RefreshAsync(TestContext.Current.CancellationToken);

        secondRefresh.ShouldBeFalse();
        cache.Current!.Catalog.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TreatsAnArchiveWithNoCatalogAsAFailedRefresh()
    {
        var source = new FakeArchiveSource().Returning(() => new TarGzBuilder()
            .WithFile("docs/ADR/orphan.md", "# Orphan\n")
            .Build());

        var (refresh, cache) = Refresh(source);

        (await refresh.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        cache.HasContent.ShouldBeFalse();
    }

    [Fact]
    public async Task SkipsAnInvalidEntryAndServesTheRest()
    {
        var source = new FakeArchiveSource().Returning(() => new TarGzBuilder()
            .WithFile("docs/index.json", """
                {
                  "documents": [
                    {
                      "id": "good",
                      "title": "Good",
                      "description": "Valid entry.",
                      "category": "ADR",
                      "status": "accepted",
                      "tags": [],
                      "path": "docs/ADR/good.md"
                    },
                    {
                      "id": "bad",
                      "title": "Bad",
                      "description": "Unknown category.",
                      "category": "Guideline",
                      "status": "accepted",
                      "tags": [],
                      "path": "docs/ADR/bad.md"
                    }
                  ]
                }
                """)
            .WithFile("docs/ADR/good.md", "# Good\n")
            .WithFile("docs/ADR/bad.md", "# Bad\n")
            .Build());

        var (refresh, cache) = Refresh(source);

        (await refresh.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        cache.Current!.Catalog.Entries.Select(entry => entry.Id).ShouldBe(["good"]);
    }

    private static Stream Archive() => new TarGzBuilder()
        .WithFile("docs/index.json", """
            {
              "documents": [
                {
                  "id": "a-decision",
                  "title": "A decision",
                  "description": "Decides something.",
                  "category": "ADR",
                  "status": "accepted",
                  "tags": ["caching"],
                  "path": "docs/ADR/a-decision.md"
                }
              ]
            }
            """)
        .WithFile("docs/ADR/a-decision.md", "# A decision\n\nBody.")
        .Build();

    private static (ContentRefreshService Refresh, DocumentSetCache Cache) Refresh(IContentArchiveSource source)
    {
        var cache = new DocumentSetCache();
        var options = new GitHubContentOptions { RefreshInterval = TimeSpan.FromMinutes(15) };

        var refresh = new ContentRefreshService(
            source,
            cache,
            new StaticOptionsMonitor(options),
            TimeProvider.System,
            NullLogger<ContentRefreshService>.Instance);

        return (refresh, cache);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<GitHubContentOptions>
    {
        public StaticOptionsMonitor(GitHubContentOptions value) => CurrentValue = value;

        public GitHubContentOptions CurrentValue { get; }

        public GitHubContentOptions Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<GitHubContentOptions, string?> listener) =>
            new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
