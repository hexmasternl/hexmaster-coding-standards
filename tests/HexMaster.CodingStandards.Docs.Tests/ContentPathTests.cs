using HexMaster.CodingStandards.Docs.Catalog;

namespace HexMaster.CodingStandards.Docs.Tests;

/// <summary>
/// The catalog comes from a public repository, so its paths are untrusted input. These cases
/// are the successors to the archive extractor's traversal and absolute-path tests: the risk
/// moved from writing outside a directory to steering a request at another path or host.
/// </summary>
public class ContentPathTests
{
    [Theory]
    [InlineData("docs/ADR/0001-a-decision.md")]
    [InlineData("docs/Designs/a-design.md")]
    [InlineData("docs/Structures/a-structure.md")]
    [InlineData("docs/ADR/a document with spaces.md")]
    [InlineData("docs/ADR/UPPERCASE.MD")]
    public void AcceptsAMarkdownFileInACategoryFolder(string path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeTrue(reason);
        reason.ShouldBeNull();
    }

    [Theory]
    [InlineData("docs/../../etc/passwd", "traversal")]
    [InlineData("docs/ADR/../../../secret.md", "traversal")]
    [InlineData("docs/ADR/./sneaky.md", "traversal")]
    public void RejectsTraversal(string path, string expectedReasonFragment)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason!.ShouldContain(expectedReasonFragment);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/docs/ADR/a.md")]
    [InlineData("C:/Windows/System32/config.md")]
    public void RejectsAbsolutePaths(string path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://evil.example/steal.md")]
    [InlineData("http://evil.example/docs/ADR/a.md")]
    [InlineData("//evil.example/docs/ADR/a.md")]
    public void RejectsASchemeOrHost(string path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason!.ShouldContain("scheme or host");
    }

    [Fact]
    public void RejectsBackslashes()
    {
        ContentPath.IsValid("docs\\ADR\\a.md", out var reason).ShouldBeFalse();
        reason!.ShouldContain("backslash");
    }

    [Theory]
    [InlineData("ADR/outside-content-root.md")]
    [InlineData("openspec/changes/a.md")]
    [InlineData("README.md")]
    public void RejectsAPathOutsideTheContentRoot(string path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("docs/notes.md")]
    [InlineData("docs/ADR/archive/old.md")]
    [InlineData("docs/Other/a.md")]
    public void RejectsAPathOutsideACategoryFolder(string path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason!.ShouldContain("docs/ADR");
    }

    [Fact]
    public void RejectsANonMarkdownFile()
    {
        ContentPath.IsValid("docs/ADR/index.json", out var reason).ShouldBeFalse();
        reason!.ShouldContain("markdown");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsAnEmptyPath(string? path)
    {
        ContentPath.IsValid(path, out var reason).ShouldBeFalse();
        reason!.ShouldContain("empty");
    }

    [Fact]
    public void EncodesEachSegmentButKeepsSeparators()
    {
        ContentPath.Encode("docs/ADR/0001-a-decision.md")
            .ShouldBe("docs/ADR/0001-a-decision.md");

        ContentPath.Encode("docs/ADR/a document with spaces.md")
            .ShouldBe("docs/ADR/a%20document%20with%20spaces.md");

        ContentPath.Encode("docs/Designs/c#-conventions.md")
            .ShouldBe("docs/Designs/c%23-conventions.md");
    }
}
