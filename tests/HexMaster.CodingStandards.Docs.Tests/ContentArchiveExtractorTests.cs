using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexMaster.CodingStandards.Docs.Tests;

public class ContentArchiveExtractorTests
{
    [Fact]
    public async Task ExtractsDocumentsAndTheCatalog()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/index.json", """{ "documents": [] }""")
            .WithFile("docs/ADR/0001-a-decision.md", "# A decision\n\nBody.")
            .WithFile("docs/Designs/a-design.md", "# A design\n")
            .WithFile("docs/Structures/a-structure.md", "# A structure\n")
            .Build();

        var content = await Extract(archive);

        content.RejectedEntries.ShouldBeEmpty();
        content.Tree.MarkdownPaths.ShouldBe([
            "docs/ADR/0001-a-decision.md",
            "docs/Designs/a-design.md",
            "docs/Structures/a-structure.md"
        ]);

        content.Tree.TryReadText(ContentArchiveExtractor.CatalogPath, out var catalog).ShouldBeTrue();
        catalog.ShouldBe("""{ "documents": [] }""");

        content.Tree.TryReadText("docs/ADR/0001-a-decision.md", out var document).ShouldBeTrue();
        document.ShouldBe("# A decision\n\nBody.");
    }

    [Fact]
    public async Task IgnoresEntriesOutsideTheContentRoot()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/index.json", "{}")
            .WithFile("src/Program.cs", "// code")
            .WithFile("README.md", "# Readme")
            .WithFile(".github/workflows/ci.yml", "on: push")
            .Build();

        var content = await Extract(archive);

        content.Tree.MarkdownPaths.ShouldBeEmpty();
        content.Tree.TryReadText("README.md", out _).ShouldBeFalse();
        content.Tree.TryReadText("src/Program.cs", out _).ShouldBeFalse();

        // Ordinary repository files are not an attack, so they are skipped quietly.
        content.RejectedEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task IgnoresDirectoryEntries()
    {
        var archive = new TarGzBuilder()
            .WithDirectory("docs/")
            .WithDirectory("docs/ADR/")
            .WithFile("docs/ADR/a.md", "# A\n")
            .Build();

        var content = await Extract(archive);

        content.Tree.MarkdownPaths.ShouldBe(["docs/ADR/a.md"]);
        content.RejectedEntries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("docs/../../etc/passwd")]
    [InlineData("docs/ADR/../../../secret.md")]
    public async Task RefusesAnEntryThatEscapesTheContentRoot(string relativePath)
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/ADR/legitimate.md", "# Legitimate\n")
            .WithFile(relativePath, "stolen")
            .Build();

        var content = await Extract(archive);

        content.RejectedEntries.ShouldHaveSingleItem().ShouldContain("..");
        content.Tree.MarkdownPaths.ShouldBe(["docs/ADR/legitimate.md"]);
    }

    [Fact]
    public async Task RefusesAnAbsolutePathEntry()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/ADR/legitimate.md", "# Legitimate\n")
            .WithRawEntry("/etc/passwd", "stolen")
            .Build();

        var content = await Extract(archive);

        content.RejectedEntries.ShouldHaveSingleItem().ShouldBe("/etc/passwd");
        content.Tree.MarkdownPaths.ShouldBe(["docs/ADR/legitimate.md"]);
    }

    [Fact]
    public async Task RefusesALinkEntryRatherThanFollowingIt()
    {
        var archive = new TarGzBuilder()
            .WithFile("docs/ADR/legitimate.md", "# Legitimate\n")
            .WithSymbolicLink("docs/ADR/sneaky.md", "../../../../etc/passwd")
            .Build();

        var content = await Extract(archive);

        content.RejectedEntries.ShouldHaveSingleItem().ShouldEndWith("docs/ADR/sneaky.md");
        content.Tree.MarkdownPaths.ShouldBe(["docs/ADR/legitimate.md"]);
        content.Tree.TryReadText("docs/ADR/sneaky.md", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractsAnArchiveWithNoCatalog()
    {
        // Extraction succeeds; it is loading that decides a missing catalog is fatal.
        var archive = new TarGzBuilder()
            .WithFile("docs/ADR/orphan.md", "# Orphan\n")
            .Build();

        var content = await Extract(archive);

        content.Tree.TryReadText(ContentArchiveExtractor.CatalogPath, out _).ShouldBeFalse();
        content.Tree.MarkdownPaths.ShouldBe(["docs/ADR/orphan.md"]);
    }

    [Fact]
    public async Task ReportsATruncatedArchiveAsUnavailableContent()
    {
        // A tarball with no end-of-archive marker: what a cut-short download looks like.
        var truncated = new TarGzBuilder().Build();

        await Should.ThrowAsync<ContentUnavailableException>(() => Extract(truncated));
    }

    [Fact]
    public async Task ReportsANonArchiveResponseAsUnavailableContent()
    {
        // GitHub serving an error page instead of a tarball must not surface as a raw
        // stream error, or the fall-back-to-cached-content path never runs.
        var notAnArchive = new MemoryStream("<html>rate limited</html>"u8.ToArray());

        await Should.ThrowAsync<ContentUnavailableException>(() => Extract(notAnArchive));
    }

    private static async Task<ExtractedContent> Extract(Stream archive)
    {
        await using (archive)
        {
            return await ContentArchiveExtractor.ExtractAsync(
                archive,
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
        }
    }
}
