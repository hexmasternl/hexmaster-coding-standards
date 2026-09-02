using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace HexMaster.CodingStandards.Docs.Tests;

public class DocumentServiceTests
{
    private const string DecisionPath = "docs/ADR/a-decision.md";
    private const string DesignPath = "docs/Designs/a-design.md";
    private const string StructurePath = "docs/Structures/a-structure.md";

    [Fact]
    public void ReportsNotReadyBeforeAnyCatalogHasLoaded()
    {
        var context = new ServiceContext();

        context.Service.IsReady.ShouldBeFalse();
        context.Service.GetIndex().Outcome.ShouldBe(DocumentOutcome.NotReady);
        context.Service.Search("anything").Outcome.ShouldBe(DocumentOutcome.NotReady);
    }

    [Fact]
    public async Task ReportsNotReadyForRetrievalBeforeAnyCatalogHasLoaded()
    {
        var context = new ServiceContext();

        var result = await context.Service.GetDocumentAsync("anything", TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.NotReady);
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public void ListsEveryDocumentWithMetadataAndNoBodies()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = context.Service.GetIndex();

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Count.ShouldBe(3);

        var first = result.Value[0];
        first.Id.ShouldBe("a-decision");
        first.Title.ShouldBe("A decision");
        first.Category.ShouldBe(DocumentCategory.Adr);
        first.Status.ShouldBe(DocumentStatus.Accepted);
        first.Tags.ShouldBe(["caching", "performance"]);

        // Listing must not touch GitHub: the catalog already holds everything it returns.
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public void OrdersTheIndexByCategoryThenId()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        context.Service.GetIndex().Value!.Select(summary => summary.Id)
            .ShouldBe(["a-decision", "a-design", "a-structure"]);
    }

    [Fact]
    public void ReturnsAnEmptyIndexForAnEmptyButLoadedCatalog()
    {
        var context = new ServiceContext().WithCatalog("""{ "documents": [] }""");

        var result = context.Service.GetIndex();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task RetrievesADocumentWithItsFullBody()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Summary.Title.ShouldBe("A decision");
        result.Value.Markdown.ShouldBe("# A decision\n\nWe will cache things.");
        result.Value.FetchedAt.ShouldBe(context.Time.GetUtcNow());
    }

    [Fact]
    public async Task FetchesOnlyTheRequestedDocumentsBody()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);

        context.Github.BodyCallsFor(DecisionPath).ShouldBe(1);
        context.Github.BodyCallsFor(DesignPath).ShouldBe(0);
        context.Github.BodyCallsFor(StructurePath).ShouldBe(0);
    }

    [Fact]
    public async Task ASecondRetrievalInsideTheLifetimeMakesNoRequest()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);
        context.Time.Advance(TimeSpan.FromMinutes(20));
        await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);

        context.Github.BodyCallsFor(DecisionPath).ShouldBe(1);
    }

    [Fact]
    public async Task ReportsNotFoundForAnUnknownIdWithNoRequest()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = await context.Service.GetDocumentAsync("no-such-document", TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.NotFound);
        result.Value.ShouldBeNull();
        result.Message!.ShouldContain("no-such-document");
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("A-Decision")]
    [InlineData("a-dec")]
    [InlineData("decision")]
    public async Task DoesNotFallBackToAFuzzyMatch(string id)
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = await context.Service.GetDocumentAsync(id, TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.NotFound);
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public async Task RejectsABlankDocumentId()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = await context.Service.GetDocumentAsync("   ", TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.InvalidRequest);
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ReportsUnavailableWhenTheFileIsMissingAtTheRef()
    {
        var context = new ServiceContext().WithLoadedCatalog();
        context.Github.WithBodyFailure(DecisionPath, ContentFetchStatus.NotFound, "GitHub has no file there");

        var result = await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);

        // Distinct from NotFound: the document is catalogued, so retrying later may work.
        result.Outcome.ShouldBe(DocumentOutcome.Unavailable);
        result.Message!.ShouldContain("a-decision");
    }

    [Fact]
    public async Task NotFoundAndUnavailableAreDistinguishableWithoutParsingMessages()
    {
        var context = new ServiceContext().WithLoadedCatalog();
        context.Github.WithBodyFailure(DecisionPath, ContentFetchStatus.RateLimited, "rate limited");

        var unknown = await context.Service.GetDocumentAsync("nope", TestContext.Current.CancellationToken);
        var unavailable = await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);

        unknown.Outcome.ShouldBe(DocumentOutcome.NotFound);
        unavailable.Outcome.ShouldBe(DocumentOutcome.Unavailable);
        unknown.Outcome.ShouldNotBe(unavailable.Outcome);
    }

    [Fact]
    public async Task ReportsUnavailableForAnUnusablePathWithoutARequest()
    {
        var context = new ServiceContext().WithCatalog(CatalogJson(
            EntryJson("traversal", "Traversal", "ADR", "docs/../../etc/passwd")));

        var result = await context.Service.GetDocumentAsync("traversal", TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(DocumentOutcome.Unavailable);
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public async Task OneBadPathLeavesEveryOtherDocumentRetrievable()
    {
        var context = new ServiceContext().WithCatalog(CatalogJson(
            EntryJson("bad", "Bad", "ADR", "docs/../escape.md"),
            EntryJson("good", "Good", "ADR", DecisionPath)));

        context.Github.WithBody(DecisionPath, "# Good\n");

        context.Service.GetIndex().Value!.Count.ShouldBe(2);
        (await context.Service.GetDocumentAsync("bad", TestContext.Current.CancellationToken))
            .Outcome.ShouldBe(DocumentOutcome.Unavailable);
        (await context.Service.GetDocumentAsync("good", TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task OneFailingDocumentDoesNotAffectAnother()
    {
        var context = new ServiceContext().WithLoadedCatalog();
        context.Github.WithBodyFailure(DecisionPath, ContentFetchStatus.Failed, "boom");

        (await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken))
            .Outcome.ShouldBe(DocumentOutcome.Unavailable);
        (await context.Service.GetDocumentAsync("a-design", TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ARecoveredDocumentIsServedOnRetry()
    {
        var context = new ServiceContext().WithLoadedCatalog();
        context.Github.WithBodyFailure(DecisionPath, ContentFetchStatus.Failed, "boom");

        (await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken))
            .Outcome.ShouldBe(DocumentOutcome.Unavailable);

        context.Github.WithBody(DecisionPath, "# A decision\n\nRecovered.");

        var result = await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value!.Markdown.ShouldContain("Recovered");
    }

    [Fact]
    public async Task ARepointedPathAfterARefreshFetchesTheNewFile()
    {
        const string RenamedPath = "docs/ADR/a-renamed-decision.md";

        var context = new ServiceContext().WithLoadedCatalog();
        context.Github.WithBody(RenamedPath, "# A decision\n\nThe new file.");

        (await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken))
            .Value!.Markdown.ShouldContain("We will cache things");

        // The catalog now points the same id at a different file.
        context.ReplaceCatalog(CatalogJson(
            EntryJson("a-decision", "A decision", "ADR", RenamedPath)));

        (await context.Service.GetDocumentAsync("a-decision", TestContext.Current.CancellationToken))
            .Value!.Markdown.ShouldContain("The new file");
    }

    [Fact]
    public void SearchesTitleTagsAndDescription()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        context.Service.Search("decision").Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
        context.Service.Search("performance").Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
        context.Service.Search("folders").Value!.Select(summary => summary.Id).ShouldBe(["a-structure"]);
    }

    [Fact]
    public void SearchDoesNotReachForBodies()
    {
        // Bodies are fetched per document and are not resident, so search is metadata-only.
        // Searching them would mean fetching the whole corpus on the first search.
        var context = new ServiceContext().WithLoadedCatalog();

        context.Service.Search("cache").Value!.ShouldBeEmpty();
        context.Github.TotalBodyCalls.ShouldBe(0);
    }

    [Fact]
    public void RanksTitleMatchesAboveTagAndDescriptionMatches()
    {
        var context = new ServiceContext().WithCatalog(CatalogJson(
            EntryJson("described", "Unrelated", "ADR", "docs/ADR/described.md",
                description: "Mentions telemetry in the description."),
            EntryJson("tagged", "Also unrelated", "ADR", "docs/ADR/tagged.md", tags: "\"telemetry\""),
            EntryJson("titled", "Telemetry", "ADR", "docs/ADR/titled.md")));

        context.Service.Search("telemetry").Value!.Select(summary => summary.Id)
            .ShouldBe(["titled", "tagged", "described"]);
    }

    [Theory]
    [InlineData("DECISION")]
    [InlineData("Decision")]
    [InlineData("dEcIsIoN")]
    public void SearchIgnoresCase(string keyword)
    {
        var context = new ServiceContext().WithLoadedCatalog();

        context.Service.Search(keyword).Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
    }

    [Fact]
    public void AnEmptySearchResultIsSuccessNotFailure()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = context.Service.Search("nothing-matches-this");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void RejectsABlankKeywordRatherThanReturningEverything(string keyword)
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var result = context.Service.Search(keyword);

        result.Outcome.ShouldBe(DocumentOutcome.InvalidRequest);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void SwapsTheCachedCatalogAtomically()
    {
        var context = new ServiceContext().WithLoadedCatalog();

        var before = context.Service.GetIndex().Value!;

        context.ReplaceCatalog(CatalogJson(EntryJson("new-one", "New one", "ADR", "docs/ADR/new.md")));

        var after = context.Service.GetIndex().Value!;

        // The snapshot a reader already holds is unaffected by the swap.
        before.Select(summary => summary.Id).ShouldBe(["a-decision", "a-design", "a-structure"]);
        after.Select(summary => summary.Id).ShouldBe(["new-one"]);
    }

    internal static string CatalogJson(params string[] entries) =>
        $"{{ \"documents\": [{string.Join(",", entries)}] }}";

    internal static string EntryJson(
        string id,
        string title,
        string category,
        string path,
        string description = "A description.",
        string status = "accepted",
        string tags = "\"template\"") =>
        $$"""
        {
          "id": "{{id}}",
          "title": "{{title}}",
          "description": "{{description}}",
          "category": "{{category}}",
          "status": "{{status}}",
          "tags": [{{tags}}],
          "path": "{{path}}"
        }
        """;

    /// <summary>Assembles the service over fakes, with the clock and GitHub under test control.</summary>
    private sealed class ServiceContext
    {
        public ServiceContext()
        {
            Github = new FakeGitHubContentClient();
            Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-02T10:00:00Z", null));
            CatalogCache = new DocumentSetCache();

            var bodyCache = new DocumentBodyCache(
                Github,
                new StaticOptionsMonitor<GitHubContentOptions>(new GitHubContentOptions()),
                Time);

            Service = new DocumentService(CatalogCache, bodyCache, NullLogger<DocumentService>.Instance);
        }

        public FakeGitHubContentClient Github { get; }

        public FakeTimeProvider Time { get; }

        public DocumentSetCache CatalogCache { get; }

        public IDocumentService Service { get; }

        public ServiceContext WithCatalog(string catalogJson)
        {
            ReplaceCatalog(catalogJson);
            return this;
        }

        public ServiceContext WithLoadedCatalog()
        {
            Github
                .WithBody(DecisionPath, "# A decision\n\nWe will cache things.")
                .WithBody(DesignPath, "# A design\n\nA pattern.")
                .WithBody(StructurePath, "# A structure\n\nFolders.");

            return WithCatalog(CatalogJson(
                EntryJson("a-decision", "A decision", "ADR", DecisionPath,
                    description: "Decides a thing.", tags: "\"caching\", \"performance\""),
                EntryJson("a-design", "A design", "Design", DesignPath),
                EntryJson("a-structure", "A structure", "Structure", StructurePath,
                    description: "Describes how folders are laid out.")));
        }

        public void ReplaceCatalog(string catalogJson) =>
            CatalogCache.Replace(DocumentSet.FromCatalogJson(catalogJson, NullLogger.Instance, Time));
    }
}
