using HexMaster.CodingStandards.Docs.Catalog;

namespace HexMaster.CodingStandards.Docs.Tests;

public class CatalogValidatorTests
{
    [Fact]
    public void PassesWhenTheCatalogAndTheTreeAgree()
    {
        var catalog = CatalogOf(
            Entry("adr-template", DocumentCategory.Adr, "docs/ADR/0000-adr-template.md", "ADR template"),
            Entry("design-template", DocumentCategory.Design, "docs/Designs/0000-design-template.md", "Design template"));

        var tree = TreeOf(
            ("docs/ADR/0000-adr-template.md", "# ADR template\n\nBody."),
            ("docs/Designs/0000-design-template.md", "# Design template\n\nBody."));

        var result = CatalogValidator.Validate(catalog, tree);

        result.Problems.ShouldBeEmpty();
        result.IsValid.ShouldBeTrue();
        result.DocumentsValidated.ShouldBe(2);
    }

    [Fact]
    public void ReportsAnEntryPointingAtAMissingDocument()
    {
        var catalog = CatalogOf(Entry("gone", DocumentCategory.Adr, "docs/ADR/deleted.md", "Gone"));

        var result = CatalogValidator.Validate(catalog, TreeOf());

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.UnresolvedPath);
        problem.Path.ShouldBe("docs/ADR/deleted.md");
    }

    [Fact]
    public void ReportsACategoryThatContradictsTheFolder()
    {
        var catalog = CatalogOf(Entry("misfiled", DocumentCategory.Adr, "docs/Designs/misfiled.md", "Misfiled"));
        var tree = TreeOf(("docs/Designs/misfiled.md", "# Misfiled\n"));

        var result = CatalogValidator.Validate(catalog, tree);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.CategoryFolderMismatch);
        problem.Message.ShouldContain("docs/Designs");
    }

    [Fact]
    public void ReportsADocumentNoEntryReferences()
    {
        var tree = TreeOf(("docs/Designs/caching-strategy.md", "# Caching strategy\n"));

        var result = CatalogValidator.Validate(DocumentCatalog.Empty, tree);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.UnindexedDocument);
        problem.Path.ShouldBe("docs/Designs/caching-strategy.md");
    }

    [Fact]
    public void ReportsADocumentOutsideTheCategoryFolders()
    {
        var tree = TreeOf(("docs/notes.md", "# Notes\n"));

        var result = CatalogValidator.Validate(DocumentCatalog.Empty, tree);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.DocumentOutsideCategoryFolder);
        problem.Path.ShouldBe("docs/notes.md");
    }

    [Fact]
    public void ReportsADocumentNestedBelowACategoryFolder()
    {
        var tree = TreeOf(("docs/ADR/archive/old.md", "# Old\n"));

        var result = CatalogValidator.Validate(DocumentCatalog.Empty, tree);

        result.Problems.ShouldHaveSingleItem()
            .Kind.ShouldBe(CatalogProblemKind.DocumentOutsideCategoryFolder);
    }

    [Fact]
    public void ReportsTitleDriftFromTheHeading()
    {
        var catalog = CatalogOf(Entry("drifted", DocumentCategory.Adr, "docs/ADR/drifted.md", "The catalog title"));
        var tree = TreeOf(("docs/ADR/drifted.md", "# A different heading\n"));

        var result = CatalogValidator.Validate(catalog, tree);

        var problem = result.Problems.ShouldHaveSingleItem();
        problem.Kind.ShouldBe(CatalogProblemKind.TitleHeadingDrift);
        problem.Message.ShouldContain("A different heading");
    }

    [Fact]
    public void ReportsADocumentWithNoLevelOneHeading()
    {
        var catalog = CatalogOf(Entry("headless", DocumentCategory.Adr, "docs/ADR/headless.md", "Headless"));
        var tree = TreeOf(("docs/ADR/headless.md", "## Only a subheading\n"));

        var result = CatalogValidator.Validate(catalog, tree);

        result.Problems.ShouldHaveSingleItem()
            .Kind.ShouldBe(CatalogProblemKind.TitleHeadingDrift);
    }

    [Theory]
    [InlineData("docs/ADR/../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("docs\\ADR\\windows.md")]
    [InlineData("ADR/outside-content-root.md")]
    [InlineData("docs/ADR/not-markdown.txt")]
    public void ReportsAPathThatIsNotAWellFormedDocumentPath(string path)
    {
        var catalog = CatalogOf(Entry("bad-path", DocumentCategory.Adr, path, "Bad path"));

        var result = CatalogValidator.Validate(catalog, TreeOf());

        result.Problems.ShouldHaveSingleItem()
            .Kind.ShouldBe(CatalogProblemKind.InvalidPath);
    }

    [Fact]
    public void CarriesParseProblemsIntoTheVerdict()
    {
        var parseProblems = new[]
        {
            new CatalogProblem(CatalogProblemKind.DuplicateId, "Entry 'x' duplicates an id.", "x")
        };

        var result = CatalogValidator.Validate(DocumentCatalog.Empty, TreeOf(), parseProblems);

        result.IsValid.ShouldBeFalse();
        result.Problems.ShouldHaveSingleItem().Kind.ShouldBe(CatalogProblemKind.DuplicateId);
    }

    [Fact]
    public void ReadsTheFirstLevelOneHeadingOnly()
    {
        CatalogValidator.ReadLevelOneHeading("# First\n\n# Second\n").ShouldBe("First");
        CatalogValidator.ReadLevelOneHeading("\n\n#  Padded  \n").ShouldBe("Padded");
        CatalogValidator.ReadLevelOneHeading("Some text\n# After prose\n").ShouldBe("After prose");
        CatalogValidator.ReadLevelOneHeading("## Not level one\n").ShouldBeNull();
        CatalogValidator.ReadLevelOneHeading("#NoSpace\n").ShouldBeNull();
    }

    private static DocumentCatalog CatalogOf(params CatalogEntry[] entries) => new(entries);

    private static CatalogEntry Entry(string id, DocumentCategory category, string path, string title) =>
        new(id, title, "A description.", category, DocumentStatus.Draft, ["template"], path);

    private static InMemoryDocumentTree TreeOf(params (string Path, string Text)[] documents) =>
        new(documents.Select(document => new KeyValuePair<string, string>(document.Path, document.Text)));
}
