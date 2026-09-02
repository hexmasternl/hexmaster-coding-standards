using System.Text.RegularExpressions;
using HexMaster.CodingStandards.Mcp.Tools;

namespace HexMaster.CodingStandards.Mcp.Tests;

/// <summary>
/// The initialization instructions: that they exist, that they fit the budget, and that
/// their load-bearing directives are present.
/// </summary>
/// <remarks>
/// This text lands in the model's system prompt on connect and causes agents to write files
/// into other people's repositories. Every assertion here is on <b>presence</b>, never on
/// exact wording: removing a directive must fail the build, and rephrasing one must not, or
/// the next person to improve a sentence learns to leave the text alone instead.
/// </remarks>
public class ServerInstructionTests
{
    /// <summary>
    /// The instructions with every run of whitespace collapsed, so where a sentence happens
    /// to wrap is not part of the contract.
    /// </summary>
    private static string Text { get; } = Regex.Replace(ServerInstructions.Text, @"\s+", " ");

    [Fact]
    public void TheInstructionsArePresentAndNonEmpty()
    {
        ServerInstructions.Text.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheInstructionsAreWithinTheSizeBudget()
    {
        // Measured on the constant as composed, not on the flattened copy: what the budget
        // buys is context in every session, and every character is sent.
        ServerInstructions.Text.Length.ShouldBeLessThanOrEqualTo(
            ServerInstructions.MaximumLength,
            $"The instructions are sent on every connect and must stay within {ServerInstructions.MaximumLength} characters. Adding to them means deciding what comes out.");
    }

    [Fact]
    public void TheServerIsIdentified()
    {
        ShouldMention("whose standards these are", "HexMaster");
        ShouldMention("what kind of documents they are", "decision records");
    }

    [Fact]
    public void TheToolsAreRelatedAsAWorkflow()
    {
        ShouldMention("how a document is discovered", "catalog", "listing", "by tag");
        ShouldMention("how its full text is obtained", "retrieve", "full text");
        ShouldMention("that a document is addressed by id", "id");
    }

    [Fact]
    public void TheFirstUseDirectiveNamesTheRecommendationTool()
    {
        // The tool name is the one string here that is not editorial: get it wrong and the
        // directive names nothing an agent can call.
        ShouldMention("the recommendation tool by name", "recommend_skills");
        ShouldMention("that this is a first-use action", "first use", "no skills", "not already");
    }

    [Fact]
    public void TheDirectiveIsConditionalOnTheWorkspace()
    {
        // Instructions are re-sent on every initialize. An unconditional directive would
        // regenerate skills every session, overwriting hand-edited ones for no change.
        ShouldMention(
            "the condition it is gated on",
            "if no skills", "not already present", "unless skills");
        ShouldMention(
            "that existing skills are left alone",
            "do not generate them again", "do not regenerate", "leave them as they are");
        ShouldMention(
            "that it is not a per-session action",
            "once-per-workspace", "every session", "every connection");
    }

    [Fact]
    public void RelevanceJudgementIsPreserved()
    {
        ShouldMention(
            "judging candidates against the repository at hand",
            "judge every candidate", "judge each candidate", "against the repository");
        ShouldMention(
            "writing only what applies",
            "only the ones that apply", "only what applies", "only those that apply");
    }

    [Fact]
    public void TheAgentIsToldItPerformsTheWrite()
    {
        ShouldMention("that the server only returns content", "content only", "only returns content");
        ShouldMention("that it cannot reach the filesystem", "no access to your filesystem", "cannot reach your filesystem");
        ShouldMention("who writes the files", "you write every file", "you write the files", "yours to write");
    }

    [Fact]
    public void NoClaimIsMadeThatTheServerTouchesTheFilesystem()
    {
        // Asserted as the absence of an affirmative claim rather than of the words
        // themselves: the text has to be able to say what the server does *not* do.
        string[] claims =
        [
            "the server writes", "the server will write", "the server creates",
            "the server installs", "the server stages", "the server tracks",
            "skills are installed", "skills will be installed", "skills are written for you",
            "generated for you", "we write", "we install", "we track"
        ];

        foreach (var claim in claims)
        {
            Text.Contains(claim, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse($"The instructions must not claim the server touches the filesystem, but say '{claim}'.");
        }
    }

    [Fact]
    public void MoreThanOneClientConventionIsNamed()
    {
        var located = new[] { ".claude/skills/", ".github/instructions/", ".cursor/rules/" }
            .Where(location => Text.Contains(location, StringComparison.Ordinal))
            .ToArray();

        located.Length.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void NoSingleClientsLocationIsPresentedAsTheOnlyDestination()
    {
        // "Include" rather than "are": the moment the list reads as exhaustive, a client that
        // is not on it has been told to write somewhere wrong.
        ShouldMention(
            "that the list is not exhaustive",
            "locations include", "such as", "for example", "among them");

        string[] exhaustivePhrasings =
        [
            "the conventional locations are", "must be written to .claude",
            "always write to .claude", "the skills location is .claude", "use .claude/skills"
        ];

        foreach (var phrasing in exhaustivePhrasings)
        {
            Text.Contains(phrasing, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse($"No client's location may be presented as the required destination, but the text says '{phrasing}'.");
        }
    }

    [Fact]
    public void AFallbackCoversAnUnlistedOrChangedConvention()
    {
        // The clause the mapping's staleness rests on. A stale row must degrade to "use your
        // own convention", never to an instruction to write somewhere wrong.
        ShouldMention("the unlisted client", "if your client is not one of these");
        ShouldMention("the changed convention", "convention has moved on");
        ShouldMention("what such a client should do", "use your client's own current convention");
    }

    [Fact]
    public void WritesAreConfinedToTheSkillsLocation()
    {
        ShouldMention(
            "that writes stay inside the skills location",
            "only inside your client's conventional skills location");
        ShouldMention("that nothing else is written", "nowhere else in the repository");
    }

    [Fact]
    public void TheAgentStatesWhatItWillGenerateBeforeWriting()
    {
        ShouldMention("that it comes before the write", "before writing", "before you write");
        ShouldMention(
            "what has to be said",
            "which skills you are generating", "what you are generating", "what skills you are generating");
        ShouldMention("where they are going", "and where");
    }

    [Fact]
    public void ThereIsNoPerSkillApprovalGate()
    {
        // A directive that demands confirmation a dozen times gets abandoned halfway, and the
        // user is left with half a skill set and no idea which half.
        ShouldMention("that approval is not sought per skill", "do not ask for approval for each skill");
    }

    [Fact]
    public void ThePerSkillContentRequirementsAreNotDuplicatedHere()
    {
        // They live in the tool response, which is paid for only once an agent has committed
        // to generating skills. Restating them here would tax every unrelated session.
        string[] requirements =
        [
            "back-reference", "distilled", "identifier derived", "when the skill applies"
        ];

        foreach (var requirement in requirements)
        {
            Text.Contains(requirement, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse($"'{requirement}' belongs in the recommendation tool's response, not in the always-resident instructions.");
            SkillAuthoringInstructions.Text
                .Contains(requirement, StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue($"'{requirement}' must still be stated in the recommendation tool's response.");
        }
    }

    [Fact]
    public void NoToolDescriptionIsRestated()
    {
        // The client already has these from the tool listing; repeating them would spend the
        // budget on something the model has twice.
        var toolDescriptions = new[]
        {
            "Lists every HexMaster coding standard with its id",
            "Returns the full markdown text of one HexMaster coding standard",
            "Finds the HexMaster coding standards carrying one subject tag",
            "Recommends which HexMaster coding standards to turn into durable agent skills"
        };

        foreach (var description in toolDescriptions)
        {
            Text.Contains(description, StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        }
    }

    private static void ShouldMention(string what, params string[] anyOf)
    {
        var found = anyOf.Any(phrase => Text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

        found.ShouldBeTrue(
            $"The instructions must state {what}, but none of [{string.Join(", ", anyOf.Select(phrase => $"'{phrase}'"))}] appears.");
    }
}
