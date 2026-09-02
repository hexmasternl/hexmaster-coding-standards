using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// Tag selection: normalisation, the exact pass, the fallback and when it does not run,
/// ordering, and the three outcomes a caller must be able to tell apart.
/// </summary>
/// <remarks>
/// Every test here runs over a fixture catalog against a fake GitHub client - no HTTP
/// handler, no host, no network. Selection never leaves the cached catalog, which is exactly
/// what makes that possible.
/// </remarks>
public class DocumentTagSelectionTests
{
    [Theory]
    [InlineData("caching")]
    [InlineData("  caching  ")]
    [InlineData("\tcaching\n")]
    public async Task IgnoresSurroundingWhitespace(string tag)
    {
        var context = TaggedCatalog();

        var selection = await context.SelectAsync(tag);

        selection.Documents.Select(document => document.Id).ShouldBe(["a-decision"]);
        selection.Match.ShouldBe(TagMatchKind.Exact);
    }

    [Theory]
    [InlineData("CACHING")]
    [InlineData("Caching")]
    [InlineData("cAcHiNg")]
    public async Task IgnoresCase(string tag)
    {
        var context = TaggedCatalog();

        var selection = await context.SelectAsync(tag);

        selection.Documents.Select(document => document.Id).ShouldBe(["a-decision"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task RejectsABlankTagRatherThanReturningTheCatalog(string tag)
    {
        var context = TaggedCatalog();

        var result = await context.Service.FindByTagAsync(tag, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.InvalidRequest);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task AnExactMatchExcludesNearMisses()
    {
        // The whole point of running exact first: a query for the ci tag must not drag in
        // cicd, and the queries an agent is least sure about are exactly the short ones.
        var context = Catalog(
            Entry("pipelines", "Pipelines", "ADR", tags: "\"ci\""),
            Entry("delivery", "Delivery", "ADR", tags: "\"cicd\""));

        var selection = await context.SelectAsync("ci");

        selection.Documents.Select(document => document.Id).ShouldBe(["pipelines"]);
        selection.Match.ShouldBe(TagMatchKind.Exact);
    }

    [Fact]
    public async Task TheFallbackAnswersAPartialTag()
    {
        var context = Catalog(
            Entry("testing-approach", "Testing approach", "Design", tags: "\"unit-testing\""),
            Entry("unrelated", "Unrelated", "Design", tags: "\"logging\""));

        var selection = await context.SelectAsync("testing");

        selection.Documents.Select(document => document.Id).ShouldBe(["testing-approach"]);
        selection.Match.ShouldBe(TagMatchKind.Fallback);
    }

    [Fact]
    public async Task ASingleCharacterStillMatchesExactly()
    {
        var context = Catalog(Entry("terse", "Terse", "ADR", tags: "\"c\""));

        var selection = await context.SelectAsync("c");

        selection.Documents.Select(document => document.Id).ShouldBe(["terse"]);
        selection.Match.ShouldBe(TagMatchKind.Exact);
    }

    [Fact]
    public async Task ASingleCharacterNeverTriggersTheFallback()
    {
        // A one-character substring hits nearly every kebab-case tag, so the fallback would
        // hand back the whole catalog looking like a narrowing.
        var context = Catalog(
            Entry("cached", "Cached", "ADR", tags: "\"caching\""),
            Entry("pipelines", "Pipelines", "ADR", tags: "\"ci\""));

        var selection = await context.SelectAsync("c");

        selection.Documents.ShouldBeEmpty();
        selection.Match.ShouldBe(TagMatchKind.None);
    }

    [Fact]
    public async Task ADocumentSatisfyingSeveralTagsIsReturnedOnce()
    {
        var context = Catalog(
            Entry("testing-approach", "Testing approach", "Design",
                tags: "\"unit-testing\", \"integration-testing\""));

        var selection = await context.SelectAsync("testing");

        selection.Documents.ShouldHaveSingleItem().Id.ShouldBe("testing-approach");
        selection.Match.ShouldBe(TagMatchKind.Fallback);
    }

    [Fact]
    public async Task NoMatchInEitherPassIsAnEmptySuccess()
    {
        var context = TaggedCatalog();

        var result = await context.Service.FindByTagAsync(
            "nothing-is-tagged-this", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Documents.ShouldBeEmpty();
        result.Value.Match.ShouldBe(TagMatchKind.None);
    }

    [Fact]
    public async Task OrdersByCategoryThenId()
    {
        var context = Catalog(
            Entry("z-structure", "Z structure", "Structure", tags: "\"shared\""),
            Entry("b-design", "B design", "Design", tags: "\"shared\""),
            Entry("a-design", "A design", "Design", tags: "\"shared\""),
            Entry("m-decision", "M decision", "ADR", tags: "\"shared\""));

        var selection = await context.SelectAsync("shared");

        selection.Documents.Select(document => document.Id)
            .ShouldBe(["m-decision", "a-design", "b-design", "z-structure"]);
    }

    [Fact]
    public async Task RepeatedSelectionsOverTheSameCatalogAreIdentical()
    {
        var context = Catalog(
            Entry("b-design", "B design", "Design", tags: "\"unit-testing\""),
            Entry("a-decision", "A decision", "ADR", tags: "\"integration-testing\""));

        var first = await context.SelectAsync("testing");
        var second = await context.SelectAsync("testing");

        // Entry by entry rather than selection by selection: the entries are records and
        // compare structurally, so this is the payload being identical - a client can diff
        // or cache two calls over the same cached catalog.
        first.Documents.ShouldBe(second.Documents);
        first.Match.ShouldBe(second.Match);
        first.Tag.ShouldBe(second.Tag);
    }

    [Fact]
    public async Task AnInvalidEntryCannotMatchAndDoesNotFailTheSelection()
    {
        var context = Catalog(
            Entry("valid", "Valid", "ADR", tags: "\"caching\""),
            Entry("invalid", "Invalid", "Nonsense", tags: "\"caching\""));

        var selection = await context.SelectAsync("caching");

        selection.Documents.Select(document => document.Id).ShouldBe(["valid"]);
    }

    [Fact]
    public async Task SelectionTouchesNoBodies()
    {
        var context = TaggedCatalog();

        await context.SelectAsync("caching");

        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AnUnloadedCatalogFailsRatherThanReturningNothing()
    {
        // "Nothing is tagged that way" and "I cannot tell you what is tagged" must never
        // look the same: an agent told the former stops asking.
        var context = new TagContext();
        context.Github.WithCatalogFailure();

        var result = await context.Service.FindByTagAsync("caching", TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.NotReady);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public async Task AStaleCachedCatalogIsStillSelectable()
    {
        var context = TaggedCatalog();
        await context.SelectAsync("caching");

        // The cache ages out and the refresh fails; the last good catalog stays in place.
        context.Github.WithCatalogFailure();
        context.Time.Advance(TimeSpan.FromMinutes(20));

        var result = await context.Service.FindByTagAsync("caching", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Documents.Select(document => document.Id).ShouldBe(["a-decision"]);
    }

    [Fact]
    public async Task ARefreshedCatalogChangesWhatATagSelects()
    {
        var context = TaggedCatalog();
        (await context.SelectAsync("caching")).Documents.Select(document => document.Id)
            .ShouldBe(["a-decision"]);

        // A refresh swapped in a catalog that tags one more document.
        context.ReplaceCatalog(
            Entry("a-decision", "A decision", "ADR", tags: "\"caching\""),
            Entry("newcomer", "Newcomer", "ADR", tags: "\"caching\""));

        (await context.SelectAsync("caching")).Documents.Select(document => document.Id)
            .ShouldBe(["a-decision", "newcomer"]);
    }

    private static TagContext TaggedCatalog() => Catalog(
        Entry("a-decision", "A decision", "ADR", tags: "\"caching\", \"performance\""),
        Entry("a-design", "A design", "Design", tags: "\"logging\""));

    private static TagContext Catalog(params string[] entries)
    {
        var context = new TagContext();
        context.Github.WithCatalog(DocumentServiceTests.CatalogJson(entries));
        return context;
    }

    private static string Entry(string id, string title, string category, string tags) =>
        DocumentServiceTests.EntryJson(id, title, category, $"docs/ADR/{id}.md", tags: tags);

    /// <summary>
    /// Assembles the service over the fake GitHub client and a controllable clock. Selection
    /// needs no HTTP handler of any kind - it never leaves the cached catalog.
    /// </summary>
    private sealed class TagContext
    {
        public TagContext()
        {
            Github = new FakeGitHubContentClient();
            Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null));
            Cache = new DocumentSetCache();

            var options = new StaticOptionsMonitor<GitHubContentOptions>(new GitHubContentOptions());
            var bodyCache = new DocumentBodyCache(Github, options, Time);

            var loader = new CatalogLoader(
                Github,
                Cache,
                bodyCache,
                options,
                Time,
                NullLogger<CatalogLoader>.Instance);

            Service = new DocumentService(Cache, bodyCache, loader, NullLogger<DocumentService>.Instance);
        }

        public FakeGitHubContentClient Github { get; }

        public FakeTimeProvider Time { get; }

        public DocumentSetCache Cache { get; }

        public IDocumentService Service { get; }

        /// <summary>Swaps the cached catalog, as a refresh would.</summary>
        public void ReplaceCatalog(params string[] entries) =>
            Cache.Replace(DocumentSet.FromCatalogJson(
                DocumentServiceTests.CatalogJson(entries),
                NullLogger.Instance,
                Time));

        public async Task<TagSelection> SelectAsync(string tag)
        {
            var result = await Service.FindByTagAsync(tag, TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            return result.Value!;
        }
    }
}
