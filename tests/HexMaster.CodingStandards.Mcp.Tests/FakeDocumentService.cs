using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;

namespace HexMaster.CodingStandards.Mcp.Tests;

/// <summary>
/// Stands in for the document service so a tool can be exercised on its own.
/// </summary>
/// <remarks>
/// A fake rather than the real service over a fixture catalog, deliberately: these tests are
/// about what a tool does with an answer, and driving the real service would let a change in
/// eligibility or ordering fail here as well as in its own suite.
/// </remarks>
internal sealed class FakeDocumentService : IDocumentService
{
    private DocumentResult<IReadOnlyList<DocumentSummary>> _candidates =
        new(DocumentOutcome.Success, []);

    /// <summary>How many times the candidate set was asked for.</summary>
    public int SkillCandidateCalls { get; private set; }

    public bool IsReady => true;

    /// <summary>Sets the candidate set to return, in the order given.</summary>
    public FakeDocumentService WithCandidates(params DocumentSummary[] candidates)
    {
        _candidates = new DocumentResult<IReadOnlyList<DocumentSummary>>(
            DocumentOutcome.Success,
            candidates);

        return this;
    }

    /// <summary>Sets the candidate request to report the given failure.</summary>
    public FakeDocumentService WithFailure(DocumentOutcome outcome, string message)
    {
        _candidates = new DocumentResult<IReadOnlyList<DocumentSummary>>(outcome, null, message);
        return this;
    }

    public Task<DocumentResult<IReadOnlyList<DocumentSummary>>> GetSkillCandidatesAsync(
        CancellationToken cancellationToken)
    {
        SkillCandidateCalls++;
        return Task.FromResult(_candidates);
    }

    public Task<DocumentResult<IReadOnlyList<DocumentSummary>>> GetIndexAsync(
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentResult<IReadOnlyList<DocumentListEntry>>> GetListingAsync(
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentResult<Document>> GetDocumentAsync(string id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DocumentResult<IReadOnlyList<DocumentSummary>>> SearchAsync(
        string keyword,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentResult<TagSelection>> FindByTagAsync(
        string tag,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Builds the candidate summaries these tests hand to a tool.</summary>
internal static class Candidates
{
    /// <param name="tags">Comma-separated, so a named argument stays readable at a call site.</param>
    public static DocumentSummary Candidate(
        string id,
        string title = "A document",
        string description = "A description of the document.",
        DocumentCategory category = DocumentCategory.Adr,
        DocumentStatus status = DocumentStatus.Accepted,
        string tags = "") =>
        new(
            id,
            title,
            description,
            category,
            status,
            tags.Length == 0 ? [] : tags.Split(',').Select(tag => tag.Trim()).ToArray());
}
