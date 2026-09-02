using System.Text.RegularExpressions;
using HexMaster.CodingStandards.Mcp.Tools;

namespace HexMaster.CodingStandards.Mcp.Tests;

/// <summary>
/// The load-bearing elements of the authoring instructions.
/// </summary>
/// <remarks>
/// The instruction text is a prompt shipped inside a server: it determines what downstream
/// agents write into other people's repositories, and it changes behaviour with no schema
/// change and no signal to any client. These tests guard the elements it must not lose in an
/// edit.
///
/// Every assertion is on <b>presence</b>, never on exact wording. The text is meant to be
/// improved editorially, and a suite that pinned its sentences would make every improvement
/// a test failure - which trains the next person to skip the improvement rather than to
/// check the element survived.
/// </remarks>
public class SkillAuthoringInstructionTests
{
    /// <summary>
    /// The instructions with every run of whitespace collapsed to a single space.
    /// </summary>
    /// <remarks>
    /// Matched against the flattened text rather than the raw constant so that where a
    /// sentence happens to wrap is not part of the contract. Without this, re-wrapping a
    /// paragraph - the most harmless edit there is - breaks assertions about phrases that now
    /// straddle a line break.
    /// </remarks>
    private static string Text { get; } =
        Regex.Replace(SkillAuthoringInstructions.Text, @"\s+", " ");

    [Fact]
    public void TheStepsAreOrderedAsAProcedure()
    {
        // Ordered, not a list of qualities: a model given qualities writes one skill per
        // candidate, and a model given steps performs the relevance step - which is the whole
        // reason the tool returns everything eligible instead of pre-filtering.
        var assess = IndexOfAny("assess the development environment", "examine the codebase");
        var decide = IndexOfAny("decide, candidate by candidate", "keep a candidate only when");
        var retrieve = IndexOfAny("retrieve the documents you kept", "for each candidate you");
        var write = IndexOfAny("write one skill per document", "every skill you write");

        assess.ShouldBeLessThan(decide);
        decide.ShouldBeLessThan(retrieve);
        retrieve.ShouldBeLessThan(write);
    }

    [Fact]
    public void TheAgentIsToldToAssessItsOwnEnvironmentFirst()
    {
        ShouldMention("the codebase being examined", "codebase", "repository");
        ShouldMention("what to look at", "languages", "frameworks");
        ShouldMention("that the assessment comes first", "before writing anything");
    }

    [Fact]
    public void TheAgentIsToldToSkipCandidatesThatDoNotApply()
    {
        ShouldMention("skipping the irrelevant", "skip any candidate that does not apply");
        ShouldMention(
            "that a standard the codebase cannot exercise is not worth a skill",
            "cannot exercise the standard",
            "cannot exercise should not be written");
    }

    [Fact]
    public void TheAgentIsToldWhyARedundantSkillIsHarmful()
    {
        // Stated rather than left implicit: an agent that does not know the cost of a
        // redundant skill treats the relevance step as optional tidiness.
        ShouldMention("the cost of a redundant skill", "redundant", "competes for attention");
    }

    [Fact]
    public void TheAgentIsToldToSkipAuthoringTemplates()
    {
        ShouldMention(
            "skipping authoring templates",
            "describes how to author a document rather than stating a standard",
            "authoring templates");
    }

    [Fact]
    public void TheAgentIsToldADraftIsProvisional()
    {
        ShouldMention("that draft is provisional", "`draft`");
        ShouldMention("how to weigh a draft", "provisional");
    }

    [Fact]
    public void TheAgentIsToldToRetrieveOnlyTheCandidatesItKeeps()
    {
        ShouldMention("the retrieval tool by name", SkillAuthoringInstructions.RetrievalToolName);
        ShouldMention("that the response carries no bodies", "metadata only", "no document content");
        ShouldMention("retrieving what was kept", "for each candidate you decided to keep");
        ShouldMention("not retrieving what was discarded", "do not retrieve the candidates you discarded");
    }

    [Fact]
    public void TheFourRequiredSkillElementsAreStated()
    {
        ShouldMention("an identifier", "identifier derived from the source document");
        ShouldMention("a description saying when the skill applies", "when the skill applies");
        ShouldMention("distilled content", "distilled from the document's actual guidance");
        ShouldMention("the back-reference", "back-reference");
    }

    [Fact]
    public void TheRequiredElementsAreStatedToBeMandatory()
    {
        ShouldMention("that the four elements are not optional", "none of them is optional");
    }

    [Fact]
    public void TheDescriptionIsRequiredToBeTriggerOriented()
    {
        // "About API design" is what a model writes by default, and it is exactly the
        // description that never matches a task - so the distinction is drawn explicitly.
        ShouldMention("when to reach for the skill", "circumstances that");
        ShouldMention(
            "the contrast with a topic description",
            "instead of when to reach for it",
            "says what the document is about");
    }

    [Fact]
    public void TheContentIsRequiredToReflectTheDocumentRatherThanItsTitle()
    {
        ShouldMention(
            "that restating the title or description is not enough",
            "not a restatement of the title or description");
        ShouldMention("what the content is made of", "rules, decisions");
    }

    [Fact]
    public void TheBackReferenceNamesTheServerTheToolAndTheDocumentId()
    {
        ShouldMention("this server", SkillAuthoringInstructions.ServerName);
        ShouldMention("the retrieval tool", SkillAuthoringInstructions.RetrievalToolName);
        ShouldMention("the document id", "source document's `id`");
    }

    [Fact]
    public void TheBackReferenceIsExplainedAsARetrievalPath()
    {
        // Without the reason, a model writes the reference as provenance decoration and a
        // later agent never follows it.
        ShouldMention(
            "why the back-reference exists",
            "retrieve the whole document when the distilled content is not enough");
    }

    [Fact]
    public void EncodingAndPlacementAreDelegatedToTheClient()
    {
        ShouldMention("that encoding follows the client", "conventions of the client");
        ShouldMention("that placement follows the client", "where it is placed");
        ShouldMention("that this is deliberate", "deliberately does not prescribe");
    }

    [Fact]
    public void NoFileFormatExtensionSchemaOrDirectoryPathIsNamed()
    {
        // A server that guesses a client's skill format produces output that client cannot
        // load, and dates the tool against every client that changes its own conventions.
        string[] formats =
        [
            "SKILL.md", ".md", ".markdown", "markdown", ".yaml", ".yml", "yaml",
            ".json", "json", ".toml", "toml", ".txt", ".xml",
            "frontmatter", "front matter", "front-matter",
            ".claude", "skills/", "/skills", "directory", "folder named", "file named",
            "serialise", "serialize"
        ];

        foreach (var format in formats)
        {
            Text.Contains(format, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse($"The instructions must name no file format or location, but mention '{format}'.");
        }
    }

    [Fact]
    public void TheContentRequirementsSurviveTheFormatDelegation()
    {
        // The delegation is the sentence most likely to be read as "anything goes". It has to
        // say the opposite about the content.
        var delegation = Text.IndexOf("deliberately does not prescribe", StringComparison.OrdinalIgnoreCase);

        delegation.ShouldBeGreaterThan(0);
        Text[delegation..].ShouldContain("remain mandatory");
    }

    private static void ShouldMention(string what, params string[] anyOf)
    {
        var found = anyOf.Any(phrase => Text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        found.ShouldBeTrue(
            $"The instructions must state {what}, but none of [{string.Join(", ", anyOf.Select(phrase => $"'{phrase}'"))}] appears.");
    }

    private static int IndexOfAny(params string[] anyOf)
    {
        var index = anyOf
            .Select(phrase => Text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase))
            .Where(position => position >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        index.ShouldBeGreaterThan(-1, $"None of [{string.Join(", ", anyOf)}] appears in the instructions.");
        return index;
    }
}
