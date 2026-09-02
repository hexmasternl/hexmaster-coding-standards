using System.ComponentModel;
using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// Returns one coding standard's full text, given its catalog id.
/// </summary>
/// <remarks>
/// A thin adapter: it translates an MCP call onto <see cref="IDocumentService"/> and shapes
/// the result. It holds no HTTP client, builds no GitHub URL, and caches nothing - all of
/// that lives in the Docs project, where it is tested.
/// </remarks>
/// <remarks>
/// <c>[McpServerToolType]</c> is what <c>WithToolsFromAssembly()</c> scans for. Without it
/// the class is invisible to discovery, the server advertises no tools capability at all, and
/// <c>tools/list</c> answers "no handler is available" - with no build error to hint at why.
/// </remarks>
[McpServerToolType]
internal sealed class GetDocumentTool
{
    private readonly IDocumentService _documents;

    public GetDocumentTool(IDocumentService documents)
    {
        _documents = documents;
    }

    [McpServerTool(Name = "get_document")]
    [Description("""
        Returns the full markdown text of one HexMaster coding standard, together with its
        title, description, category, status, and tags.

        The id must be a document id from the coding standards catalog; it is not a filename,
        a path, or a title. Ids are exact and case-sensitive.
        """)]
    public async Task<CallToolResult> GetDocumentAsync(
        [Description("Exact document id from the coding standards catalog, for example 'adopt-dotnet-10'.")]
        string id,
        CancellationToken cancellationToken)
    {
        // Rejected before any lookup or network call: a blank id can only be a caller bug.
        if (string.IsNullOrWhiteSpace(id))
        {
            return Error("A document id is required. List the catalog to find one.");
        }

        var result = await _documents.GetDocumentAsync(id, cancellationToken).ConfigureAwait(false);

        // Failures come back as tool results flagged isError, not protocol errors, so the
        // model sees them in the tool result and can act on them. The two failures are worded
        // differently on purpose: a model that cannot tell "this does not exist" from "the
        // server could not fetch it" will retry the wrong one.
        return result.Outcome switch
        {
            DocumentOutcome.Success => Success(result.Value!),

            DocumentOutcome.NotFound => Error(
                $"No coding standard is catalogued with id '{id}'. Ids are exact and case-sensitive; list the catalog to find the right one."),

            DocumentOutcome.Unavailable => Error(
                $"The coding standard '{id}' exists in the catalog, but its content could not be retrieved. {result.Message} This may be temporary — retrying later may succeed."),

            DocumentOutcome.NotReady => Error(
                "The coding standards have not finished loading yet. Retry in a few seconds."),

            _ => Error(result.Message ?? $"The request for '{id}' could not be handled.")
        };
    }

    /// <summary>
    /// Renders the document as the metadata a reader needs to judge it, followed by the
    /// markdown verbatim - untruncated and unsummarised, because a standard read in part is
    /// worse than one not read at all.
    /// </summary>
    private static CallToolResult Success(Document document)
    {
        var summary = document.Summary;

        var text = $"""
            # {summary.Title}

            - **Id**: {summary.Id}
            - **Category**: {DocumentCategories.CatalogValueFor(summary.Category)}
            - **Status**: {DocumentStatuses.CatalogValueFor(summary.Status)}
            - **Tags**: {(summary.Tags.Count == 0 ? "(none)" : string.Join(", ", summary.Tags))}
            - **Description**: {summary.Description}

            ---

            {document.Markdown}
            """;

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }]
        };
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
