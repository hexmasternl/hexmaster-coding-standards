using System.Text.RegularExpressions;
using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Mcp.Tools;
using ModelContextProtocol.Protocol;
using static HexMaster.CodingStandards.Mcp.Tests.Candidates;

namespace HexMaster.CodingStandards.Mcp.Tests;

/// <summary>
/// What the recommendation tool returns: the six-field projection, the completeness of the
/// candidate set, and the three outcomes a caller must be able to tell apart.
/// </summary>
/// <remarks>
/// The fake document service throws from every member except the candidate set, so a tool
/// that reached for a document body or a listing fails these tests rather than passing them
/// quietly.
/// </remarks>
public class RecommendSkillsToolTests
{
    [Fact]
    public async Task ACandidateCarriesTheSixMetadataFields()
    {
        var text = await CandidateSection(Candidate(
            "a-decision",
            "A decision",
            "Decides a thing.",
            DocumentCategory.Adr,
            DocumentStatus.Accepted,
            "caching, performance"));

        text.ShouldContain("A decision");
        text.ShouldContain("**Id**: a-decision");
        text.ShouldContain("**Category**: ADR");
        text.ShouldContain("**Status**: accepted");
        text.ShouldContain("**Tags**: caching, performance");
        text.ShouldContain("**Description**: Decides a thing.");
    }

    [Fact]
    public async Task ACandidateCarriesNothingElse()
    {
        // Asserted as the exact set of labelled fields rather than by reading the renderer,
        // so widening the payload every client sees fails here first.
        var text = await CandidateSection(Candidate("a-decision", tags: "caching"));

        LabelledFieldsIn(text).ShouldBe(["Id", "Category", "Status", "Tags", "Description"]);
    }

    [Fact]
    public async Task NoBodyPathOrRepositoryReferenceAppearsAnywhere()
    {
        // A path or a raw URL would invite an agent to fetch the file itself and bypass the
        // retrieval tool the generated back-references depend on.
        var text = await ResponseText(
            Candidate("a-decision", "A decision", category: DocumentCategory.Adr),
            Candidate("a-design", "A design", category: DocumentCategory.Design));

        text.ShouldNotContain("docs/");
        text.ShouldNotContain(".md");
        text.ShouldNotContain("github.com");
        text.ShouldNotContain("raw.githubusercontent");
        text.ShouldNotContain("index.json");
    }

    [Fact]
    public async Task StatusIsCarriedThroughSoADraftCanBeWeighedDifferently()
    {
        var text = await CandidateSection(
            Candidate("settled", "Settled", status: DocumentStatus.Accepted),
            Candidate("being-settled", "Being settled", status: DocumentStatus.Draft));

        text.ShouldContain("**Status**: draft");
        text.ShouldContain("**Status**: accepted");
    }

    [Fact]
    public async Task ADocumentWithNoTagsStillShowsATagsField()
    {
        var text = await CandidateSection(Candidate("a-decision"));

        LabelledFieldsIn(text).ShouldContain("Tags");
        text.ShouldContain("**Tags**: (none)");
    }

    [Fact]
    public async Task EveryCandidateTheServiceReportsAppearsExactlyOnce()
    {
        var text = await CandidateSection(
            Candidate("a-decision", "A decision", category: DocumentCategory.Adr),
            Candidate("b-decision", "B decision", category: DocumentCategory.Adr),
            Candidate("a-design", "A design", category: DocumentCategory.Design),
            Candidate("a-structure", "A structure", category: DocumentCategory.Structure));

        IdsIn(text).ShouldBe(["a-decision", "b-decision", "a-design", "a-structure"]);
    }

    [Fact]
    public async Task NoCandidateIsSuppressedBySubjectMatter()
    {
        // The tool cannot see the caller's codebase, so it must not guess that a styling
        // standard is useless to it. Returning everything is what leaves that judgement with
        // the only party able to make it.
        var text = await CandidateSection(
            Candidate("frontend-styling", "Frontend styling variables", category: DocumentCategory.Design, tags: "css"),
            Candidate("eventual-consistency", "Eventual consistency", category: DocumentCategory.Adr, tags: "messaging"),
            Candidate("adr-template", "ADR template", category: DocumentCategory.Structure, tags: "template"));

        IdsIn(text).ShouldBe(["frontend-styling", "eventual-consistency", "adr-template"]);
    }

    [Fact]
    public async Task CandidatesAppearInTheOrderTheServiceReturnedThem()
    {
        // No sort of its own: ordering is the service's guarantee, and re-sorting here would
        // make two components responsible for one promise.
        var text = await CandidateSection(
            Candidate("m-decision", "M decision", category: DocumentCategory.Adr),
            Candidate("a-design", "A design", category: DocumentCategory.Design),
            Candidate("b-design", "B design", category: DocumentCategory.Design),
            Candidate("z-structure", "Z structure", category: DocumentCategory.Structure));

        IdsIn(text).ShouldBe(["m-decision", "a-design", "b-design", "z-structure"]);
    }

    [Fact]
    public async Task TheResponseCarriesTheAuthoringInstructions()
    {
        var text = await ResponseText(Candidate("a-decision"));

        text.ShouldContain(SkillAuthoringInstructions.Text);
    }

    [Fact]
    public async Task AnEmptyCandidateSetIsASuccessStatingThereIsNothingToGenerate()
    {
        var result = await Invoke(new FakeDocumentService());

        ShouldBeSuccess(result);
        TextOf(result).ShouldContain("nothing to generate");
    }

    [Fact]
    public async Task AnUnloadedCatalogIsAnErrorRatherThanAnEmptyCandidateSet()
    {
        // "Nothing is eligible" and "I cannot tell you what is eligible" must never look the
        // same: an agent told the former writes no skills and stops asking.
        var documents = new FakeDocumentService()
            .WithFailure(DocumentOutcome.NotReady, "not loaded");

        var result = await Invoke(documents);

        ShouldBeError(result);
        TextOf(result).ShouldContain("not available yet");
        TextOf(result).ShouldNotContain("nothing to generate");
    }

    [Fact]
    public async Task AnyOtherFailureIsAlsoAToolErrorRatherThanAProtocolError()
    {
        var documents = new FakeDocumentService()
            .WithFailure(DocumentOutcome.Unavailable, "GitHub is unreachable.");

        var result = await Invoke(documents);

        ShouldBeError(result);
        TextOf(result).ShouldContain("GitHub is unreachable.");
    }

    [Fact]
    public async Task TwoInvocationsOverTheSameCatalogReturnTheSameResponse()
    {
        // Nothing is written and nothing is remembered, so the second call cannot differ from
        // the first.
        var documents = new FakeDocumentService().WithCandidates(
            Candidate("a-decision", "A decision", category: DocumentCategory.Adr),
            Candidate("a-design", "A design", category: DocumentCategory.Design));

        var first = TextOf(await Invoke(documents));
        var second = TextOf(await Invoke(documents));

        second.ShouldBe(first);
        documents.SkillCandidateCalls.ShouldBe(2);
    }

    [Fact]
    public void TheToolHoldsNoSelectionLogicOfItsOwn()
    {
        // The tool's only document dependency is the service interface. A second one would
        // mean it had grown a way to reach documents without going through the layer that
        // owns eligibility and ordering.
        typeof(RecommendSkillsTool)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(IDocumentService)]);
    }

    /// <summary>The tool's whole response text for a candidate set.</summary>
    private static async Task<string> ResponseText(params DocumentSummary[] candidates)
    {
        var result = await Invoke(new FakeDocumentService().WithCandidates(candidates));

        ShouldBeSuccess(result);
        return TextOf(result);
    }

    /// <summary>
    /// The candidate list alone, with the fixed instruction text cut off, so an assertion
    /// about the projection cannot be satisfied by a phrase in the instructions.
    /// </summary>
    private static async Task<string> CandidateSection(params DocumentSummary[] candidates)
    {
        var text = await ResponseText(candidates);
        var instructions = text.IndexOf(SkillAuthoringInstructions.Text, StringComparison.Ordinal);

        instructions.ShouldBeGreaterThan(0);
        return text[..instructions];
    }

    /// <summary>
    /// Asserts a successful tool result. <c>IsError</c> is nullable and left unset on
    /// success, so "not an error" is the assertion and null is one of its passing values.
    /// </summary>
    private static void ShouldBeSuccess(CallToolResult result) =>
        (result.IsError ?? false).ShouldBeFalse();

    private static void ShouldBeError(CallToolResult result) =>
        (result.IsError ?? false).ShouldBeTrue();

    private static async Task<CallToolResult> Invoke(FakeDocumentService documents) =>
        await new RecommendSkillsTool(documents).RecommendSkillsAsync(TestContext.Current.CancellationToken);

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    /// <summary>The labelled fields rendered for the first candidate, in order.</summary>
    private static IReadOnlyList<string> LabelledFieldsIn(string candidateSection) =>
        Regex.Matches(candidateSection, @"\*\*(\w+)\*\*:")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The candidate ids in the order they were rendered.</summary>
    private static IReadOnlyList<string> IdsIn(string candidateSection) =>
        Regex.Matches(candidateSection, @"\*\*Id\*\*: (?<id>[a-z0-9-]+)")
            .Select(match => match.Groups["id"].Value)
            .ToArray();
}
