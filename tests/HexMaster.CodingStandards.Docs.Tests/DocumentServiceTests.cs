using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexMaster.CodingStandards.Docs.Tests;

public class DocumentServiceTests
{
    [Fact]
    public void ReportsNotReadyBeforeAnythingHasLoaded()
    {
        var (service, _) = Service();

        service.IsReady.ShouldBeFalse();
        service.GetIndex().Outcome.ShouldBe(DocumentOutcome.NotReady);
        service.GetDocument("anything").Outcome.ShouldBe(DocumentOutcome.NotReady);
        service.Search("anything").Outcome.ShouldBe(DocumentOutcome.NotReady);
    }

    [Fact]
    public void ListsEveryDocumentWithMetadataAndNoBodies()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var result = service.GetIndex();

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Count.ShouldBe(3);

        var first = result.Value[0];
        first.Id.ShouldBe("a-decision");
        first.Title.ShouldBe("A decision");
        first.Description.ShouldBe("Decides a thing about caching.");
        first.Category.ShouldBe(DocumentCategory.Adr);
        first.Status.ShouldBe(DocumentStatus.Accepted);
        first.Tags.ShouldBe(["caching", "performance"]);
    }

    [Fact]
    public void OrdersTheIndexByCategoryThenId()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        service.GetIndex().Value!.Select(summary => summary.Id)
            .ShouldBe(["a-decision", "a-design", "a-structure"]);
    }

    [Fact]
    public void ReturnsAnEmptyIndexForAnEmptyButLoadedCatalog()
    {
        var (service, cache) = Service();
        cache.Replace(SetFrom(new TarGzBuilder().WithFile("docs/index.json", """{ "documents": [] }""")));

        var result = service.GetIndex();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public void RetrievesADocumentWithItsFullBody()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var result = service.GetDocument("a-decision");

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Summary.Title.ShouldBe("A decision");
        result.Value.Markdown.ShouldBe("# A decision\n\nWe will cache things.");
    }

    [Fact]
    public void ReportsNotFoundForAnUnknownId()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var result = service.GetDocument("no-such-document");

        result.Outcome.ShouldBe(DocumentOutcome.NotFound);
        result.Value.ShouldBeNull();
        result.Message!.ShouldContain("no-such-document");
    }

    [Theory]
    [InlineData("A-Decision")]
    [InlineData("a-dec")]
    [InlineData("decision")]
    public void DoesNotFallBackToAFuzzyMatch(string id)
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        service.GetDocument(id).Outcome.ShouldBe(DocumentOutcome.NotFound);
    }

    [Fact]
    public void RejectsABlankDocumentId()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        service.GetDocument("   ").Outcome.ShouldBe(DocumentOutcome.InvalidRequest);
    }

    [Fact]
    public void DropsACataloguedDocumentTheArchiveDoesNotContain()
    {
        // Task 4.6: the catalog lists two documents, the archive carries one body.
        var archive = new TarGzBuilder()
            .WithFile("docs/index.json", CatalogJson(
                EntryJson("present", "Present", "ADR", "docs/ADR/present.md"),
                EntryJson("absent", "Absent", "ADR", "docs/ADR/absent.md")))
            .WithFile("docs/ADR/present.md", "# Present\n");

        var (service, cache) = Service();
        cache.Replace(SetFrom(archive));

        service.GetDocument("present").IsSuccess.ShouldBeTrue();
        service.GetDocument("absent").Outcome.ShouldBe(DocumentOutcome.NotFound);
        service.GetIndex().Value!.Select(summary => summary.Id).ShouldBe(["present"]);
    }

    [Fact]
    public void SearchesTitleTagsDescriptionAndBody()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        service.Search("decision").Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
        service.Search("performance").Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
        service.Search("caching").Value!.Select(summary => summary.Id).ShouldContain("a-decision");
        service.Search("folders").Value!.Select(summary => summary.Id).ShouldBe(["a-structure"]);
    }

    [Fact]
    public void RanksTitleMatchesAboveBodyOnlyMatches()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/index.json", CatalogJson(
                EntryJson("body-match", "Something else", "ADR", "docs/ADR/body.md"),
                EntryJson("telemetry", "Telemetry", "ADR", "docs/ADR/title.md")))
            .WithFile("docs/ADR/body.md", "# Something else\n\nMentions telemetry deep in the body.")
            .WithFile("docs/ADR/title.md", "# Telemetry\n\nNothing else.");

        var (service, cache) = Service();
        cache.Replace(SetFrom(archive));

        service.Search("telemetry").Value!.Select(summary => summary.Id)
            .ShouldBe(["telemetry", "body-match"]);
    }

    [Fact]
    public void RanksTagMatchesAboveBodyOnlyMatches()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/index.json", CatalogJson(
                EntryJson("body-match", "Unrelated", "ADR", "docs/ADR/body.md", tags: "\"other\""),
                EntryJson("tagged", "Also unrelated", "ADR", "docs/ADR/tagged.md", tags: "\"bicep\"")))
            .WithFile("docs/ADR/body.md", "# Unrelated\n\nA passing mention of bicep.")
            .WithFile("docs/ADR/tagged.md", "# Also unrelated\n\nNothing.");

        var (service, cache) = Service();
        cache.Replace(SetFrom(archive));

        service.Search("bicep").Value!.Select(summary => summary.Id).ShouldBe(["tagged", "body-match"]);
    }

    [Theory]
    [InlineData("DECISION")]
    [InlineData("Decision")]
    [InlineData("dEcIsIoN")]
    public void SearchIgnoresCase(string keyword)
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        service.Search(keyword).Value!.Select(summary => summary.Id).ShouldBe(["a-decision"]);
    }

    [Fact]
    public void AnEmptySearchResultIsSuccessNotFailure()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var result = service.Search("nothing-matches-this");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void RejectsABlankKeywordRatherThanReturningEverything(string keyword)
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var result = service.Search(keyword);

        result.Outcome.ShouldBe(DocumentOutcome.InvalidRequest);
        result.Value.ShouldBeNull();
    }

    [Fact]
    public void SwapsTheCachedSetAtomically()
    {
        var (service, cache) = Service();
        cache.Replace(LoadedSet());

        var before = service.GetIndex().Value!;

        cache.Replace(SetFrom(new TarGzBuilder()
            .WithFile("docs/index.json", CatalogJson(EntryJson("new-one", "New one", "ADR", "docs/ADR/new.md")))
            .WithFile("docs/ADR/new.md", "# New one\n")));

        var after = service.GetIndex().Value!;

        // The snapshot a reader already holds is unaffected by the swap.
        before.Select(summary => summary.Id).ShouldBe(["a-decision", "a-design", "a-structure"]);
        after.Select(summary => summary.Id).ShouldBe(["new-one"]);
    }

    private static (IDocumentService Service, DocumentSetCache Cache) Service()
    {
        var cache = new DocumentSetCache();
        return (new DocumentService(cache), cache);
    }

    private static DocumentSet LoadedSet() => SetFrom(new TarGzBuilder()
        .WithFile("docs/index.json", CatalogJson(
            EntryJson("a-decision", "A decision", "ADR", "docs/ADR/a-decision.md",
                description: "Decides a thing about caching.", tags: "\"caching\", \"performance\""),
            EntryJson("a-design", "A design", "Design", "docs/Designs/a-design.md"),
            EntryJson("a-structure", "A structure", "Structure", "docs/Structures/a-structure.md",
                description: "Describes how folders are laid out.")))
        .WithFile("docs/ADR/a-decision.md", "# A decision\n\nWe will cache things.")
        .WithFile("docs/Designs/a-design.md", "# A design\n\nA pattern.")
        .WithFile("docs/Structures/a-structure.md", "# A structure\n\nFolders."));

    private static DocumentSet SetFrom(TarGzBuilder builder)
    {
        using var archive = builder.Build();

        var extracted = ContentArchiveExtractor
            .ExtractAsync(archive, NullLogger.Instance, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();

        return DocumentSet.FromExtractedContent(extracted, NullLogger.Instance, TimeProvider.System);
    }

    private static string CatalogJson(params string[] entries) =>
        $"{{ \"documents\": [{string.Join(",", entries)}] }}";

    private static string EntryJson(
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
}
