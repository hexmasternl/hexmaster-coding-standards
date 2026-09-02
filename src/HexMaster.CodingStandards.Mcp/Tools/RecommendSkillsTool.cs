using System.ComponentModel;
using System.Text;
using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// Returns the HexMaster coding standards as candidates for agent skills, with instructions
/// for turning them into skills.
/// </summary>
/// <remarks>
/// Different in kind from its siblings: <c>list_documents</c>, <c>find_documents_by_tag</c>,
/// and <see cref="GetDocumentTool"/> answer questions, and this one asks the caller to go and
/// do something. Most of its payload is <see cref="SkillAuthoringInstructions"/>.
///
/// A thin adapter all the same - it holds no eligibility rule, no filter, and no sort. The
/// candidate set comes from the document service, which decides what is eligible and in what
/// order, and this class projects six fields and appends fixed text. In particular it does
/// not judge relevance: returning everything eligible is what leaves that judgement with the
/// only party that can see the caller's codebase.
///
/// Nothing is written and nothing is remembered. Two calls over the same cached catalog
/// produce the same bytes.
///
/// <c>[McpServerToolType]</c> is what <c>WithToolsFromAssembly()</c> scans for. Without it
/// the class is invisible to discovery and the tool silently never appears, with nothing at
/// build time to say so.
/// </remarks>
[McpServerToolType]
internal sealed class RecommendSkillsTool
{
    private readonly IDocumentService _documents;

    public RecommendSkillsTool(IDocumentService documents)
    {
        _documents = documents;
    }

    [McpServerTool(Name = "recommend_skills")]
    [Description("""
        Recommends which HexMaster coding standards to turn into durable agent skills for the
        repository you are working in, and explains how to write them. Returns one candidate
        per eligible standard - id, title, description, category, status, tags - together with
        the authoring instructions. No document text.

        Unlike get_document, list_documents, and find_documents_by_tag, this does not answer a
        question about the catalog: it hands you a shortlist and a procedure. Call it when
        setting a repository up to follow the HexMaster standards by default, rather than
        looking them up when someone remembers to. It takes no arguments, returns every
        candidate without judging which apply to your codebase - that judgement is yours - and
        tells you to fetch the full text with get_document only for the ones you keep.
        """)]
    public async Task<CallToolResult> RecommendSkillsAsync(CancellationToken cancellationToken)
    {
        // No parameters at all, and none worth adding: a category or tag filter would let an
        // agent narrow the set before it has looked at its own codebase, which is the one
        // step that makes the result useful. Any argument a client sends anyway is simply
        // not bound.
        var result = await _documents.GetSkillCandidatesAsync(cancellationToken).ConfigureAwait(false);

        // Failures come back as tool results flagged isError rather than protocol errors, so
        // the model sees them and can react - the same contract the sibling tools keep.
        return result.Outcome switch
        {
            DocumentOutcome.Success => Success(result.Value!),

            // Never an empty candidate set: "nothing is eligible" and "I cannot tell you
            // what is eligible" are different answers, and an agent told the former writes
            // no skills and stops asking.
            DocumentOutcome.NotReady => Error(
                "The coding standards catalog is not available yet - it has not finished loading from GitHub. Retry in a few seconds."),

            _ => Error(result.Message ?? "The coding standards could not be recommended as skills.")
        };
    }

    /// <summary>
    /// Renders the candidates followed by the authoring instructions, in the order the
    /// service returned - which is category then id, and stable across calls.
    /// </summary>
    private static CallToolResult Success(IReadOnlyList<DocumentSummary> candidates)
    {
        if (candidates.Count == 0)
        {
            // A success, not an error. Every standard being retired is an answer, and the
            // correct response to it is to write nothing.
            return Text(
                "The coding standards catalog loaded successfully and holds no standard worth turning into a skill: every catalogued document is superseded or deprecated. There is nothing to generate.");
        }

        var text = new StringBuilder();

        text.Append("# Skill candidates from the HexMaster coding standards (")
            .Append(candidates.Count)
            .AppendLine(candidates.Count == 1 ? " candidate)" : " candidates)")
            .AppendLine()
            .AppendLine(
                "Every current HexMaster coding standard, as a candidate for a skill. This is a shortlist, not a work list - the instructions after it tell you how to decide which of these to write and how to write them.");

        // The service returns category-then-id order, so grouping is a single pass.
        DocumentCategory? category = null;

        foreach (var candidate in candidates)
        {
            if (category != candidate.Category)
            {
                category = candidate.Category;
                text.AppendLine()
                    .Append("## ")
                    .AppendLine(DocumentCategories.CatalogValueFor(candidate.Category));
            }

            // Six fields and nothing else: no body, no repository path, no URL. A path would
            // invite an agent to fetch the file itself and bypass the retrieval tool the
            // back-reference depends on.
            text.AppendLine()
                .Append("### ")
                .AppendLine(candidate.Title)
                .AppendLine()
                .Append("- **Id**: ")
                .AppendLine(candidate.Id)
                .Append("- **Category**: ")
                .AppendLine(DocumentCategories.CatalogValueFor(candidate.Category))
                .Append("- **Status**: ")
                .AppendLine(DocumentStatuses.CatalogValueFor(candidate.Status))
                .Append("- **Tags**: ")
                .AppendLine(candidate.Tags.Count == 0 ? "(none)" : string.Join(", ", candidate.Tags))
                .Append("- **Description**: ")
                .AppendLine(candidate.Description);
        }

        text.AppendLine()
            .AppendLine("---")
            .AppendLine()
            .AppendLine(SkillAuthoringInstructions.Text);

        return Text(text.ToString());
    }

    private static CallToolResult Text(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }]
    };

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
