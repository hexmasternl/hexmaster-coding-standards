using HexMaster.CodingStandards.Docs.Catalog;

namespace HexMaster.CodingStandards.Docs.Tests;

public class CatalogParserTests
{
    private const string ValidCatalog = """
        {
          "documents": [
            {
              "id": "adr-template",
              "title": "ADR template",
              "description": "The expected shape of an architecture decision record.",
              "category": "ADR",
              "status": "draft",
              "tags": ["template", "authoring"],
              "path": "docs/ADR/0000-adr-template.md"
            }
          ]
        }
        """;

    [Fact]
    public void ParsesAValidCatalog()
    {
        var result = CatalogParser.Parse(ValidCatalog);

        result.Problems.ShouldBeEmpty();
        result.Catalog.Count.ShouldBe(1);

        var entry = result.Catalog.Entries[0];
        entry.Id.ShouldBe("adr-template");
        entry.Title.ShouldBe("ADR template");
        entry.Category.ShouldBe(DocumentCategory.Adr);
        entry.Status.ShouldBe(DocumentStatus.Draft);
        entry.Tags.ShouldBe(["template", "authoring"]);
        entry.Path.ShouldBe("docs/ADR/0000-adr-template.md");
    }

    [Fact]
    public void OrdersEntriesByCategoryThenId()
    {
        var json = Catalog(
            Entry("zeta-structure", category: "Structure", path: "docs/Structures/z.md"),
            Entry("beta-design", category: "Design", path: "docs/Designs/b.md"),
            Entry("alpha-design", category: "Design", path: "docs/Designs/a.md"),
            Entry("omega-adr", category: "ADR", path: "docs/ADR/o.md"));

        var result = CatalogParser.Parse(json);

        result.Catalog.Entries.Select(entry => entry.Id)
            .ShouldBe(["omega-adr", "alpha-design", "beta-design", "zeta-structure"]);
    }

    [Fact]
    public void FailsOnUnparseableJson()
    {
        var exception = Should.Throw<CatalogFormatException>(() => CatalogParser.Parse("{ not json"));

        exception.Message.ShouldContain("not valid JSON");
    }

    [Fact]
    public void FailsWhenTheDocumentsArrayIsAbsent()
    {
        Should.Throw<CatalogFormatException>(() => CatalogParser.Parse("""{ "other": [] }"""))
            .Message.ShouldContain("'documents'");
    }

    [Fact]
    public void SkipsAnEntryWithAnUnknownCategoryAndKeepsTheRest()
    {
        var json = Catalog(
            Entry("good-one"),
            Entry("bad-one", category: "Guideline"));

        var result = CatalogParser.Parse(json);

        result.Catalog.Entries.Select(entry => entry.Id).ShouldBe(["good-one"]);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.UnknownCategory);
        problem.EntryId.ShouldBe("bad-one");
        problem.Message.ShouldContain("'ADR'");
    }

    [Fact]
    public void SkipsAnEntryWithAnUnknownStatus()
    {
        var result = CatalogParser.Parse(Catalog(Entry("in-review", status: "in-review")));

        result.Catalog.Count.ShouldBe(0);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.UnknownStatus);
        problem.Message.ShouldContain("'superseded'");
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("path")]
    [InlineData("tags")]
    public void ReportsAMissingRequiredProperty(string property)
    {
        var json = Catalog(Entry("incomplete").Replace($"\"{property}\":", "\"ignored\":", StringComparison.Ordinal));

        var result = CatalogParser.Parse(json);

        result.Catalog.Count.ShouldBe(0);
        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.MissingProperty);
        problem.Message.ShouldContain($"'{property}'");
    }

    [Fact]
    public void ReportsAnEntryWithNoIdByPosition()
    {
        var json = Catalog(Entry("nameless").Replace("\"id\":", "\"ignored\":", StringComparison.Ordinal));

        var result = CatalogParser.Parse(json);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.MissingProperty);
        problem.Message.ShouldContain("position 0");
    }

    [Fact]
    public void SkipsADuplicateIdRatherThanShadowingTheFirstEntry()
    {
        var json = Catalog(
            Entry("same-id", title: "First", path: "docs/ADR/first.md"),
            Entry("same-id", title: "Second", path: "docs/ADR/second.md"));

        var result = CatalogParser.Parse(json);

        var entry = result.Catalog.Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("First");

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.DuplicateId);
        problem.EntryId.ShouldBe("same-id");
    }

    [Fact]
    public void LooksUpAnEntryByExactId()
    {
        var catalog = CatalogParser.Parse(Catalog(Entry("adr-template"))).Catalog;

        catalog.TryGetEntry("adr-template", out var found).ShouldBeTrue();
        found!.Id.ShouldBe("adr-template");

        catalog.TryGetEntry("ADR-Template", out _).ShouldBeFalse();
        catalog.TryGetEntry("adr", out _).ShouldBeFalse();
    }

    private static string Catalog(params string[] entries) =>
        $"{{ \"documents\": [{string.Join(",", entries)}] }}";

    private static string Entry(
        string id,
        string title = "A title",
        string description = "A description.",
        string category = "ADR",
        string status = "draft",
        string path = "docs/ADR/a-document.md") =>
        $$"""
        {
          "id": "{{id}}",
          "title": "{{title}}",
          "description": "{{description}}",
          "category": "{{category}}",
          "status": "{{status}}",
          "tags": ["template"],
          "path": "{{path}}"
        }
        """;
}
