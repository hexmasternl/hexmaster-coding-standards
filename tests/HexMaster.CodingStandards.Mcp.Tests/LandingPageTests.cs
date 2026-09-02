using HexMaster.CodingStandards.Mcp;

namespace HexMaster.CodingStandards.Mcp.Tests;

/// <summary>
/// The page a browser gets at the root: that it is a whole document, and that it says the
/// two things it exists to say.
/// </summary>
/// <remarks>
/// Assertions are on presence, not wording, for the same reason as
/// <see cref="ServerInstructionTests"/>: rewording the copy must not fail the build, but
/// shipping a page that no longer names the server or links to the repository must.
///
/// The middleware that serves it is not covered here. Telling a browser's GET from an MCP
/// client's would mean a host, and the project's tests deliberately run without one - it
/// was verified by hand instead: a browser Accept gets the page, `Accept:
/// text/event-stream` falls through to MCP, and POST / still completes an initialize
/// handshake.
/// </remarks>
public class LandingPageTests
{
    [Fact]
    public void ThePageIsAWholeHtmlDocument()
    {
        LandingPage.Html.ShouldStartWith("<!doctype html>");
        LandingPage.Html.TrimEnd().ShouldEndWith("</html>");
    }

    [Fact]
    public void ThePageSaysWhichServerWasReached()
    {
        LandingPage.Html.ShouldContain("HexMaster Coding Standards");
        LandingPage.Html.ShouldContain("MCP server");
    }

    [Fact]
    public void ThePageLinksToTheRepository()
    {
        LandingPage.Html.ShouldContain($"href=\"{LandingPage.RepositoryUrl}\"");
    }

    [Fact]
    public void ThePageReferencesNothingItCannotServeItself()
    {
        // Everything is inline on purpose: the container has no static file middleware and
        // no assets, so an external stylesheet, script, or image would render as a gap.
        LandingPage.Html.ShouldNotContain("<link");
        LandingPage.Html.ShouldNotContain("<script");
        LandingPage.Html.ShouldNotContain("<img");
    }

    [Fact]
    public void EveryInterpolationInThePageWasSubstituted()
    {
        // The page is a $$"""...""" literal so the repository URL appears once. Two
        // adjacent CSS braces would turn into an interpolation hole and take a chunk of
        // the stylesheet with them, so check none survived into the output.
        LandingPage.Html.ShouldNotContain("{{");
        LandingPage.Html.ShouldNotContain("}}");
    }
}
