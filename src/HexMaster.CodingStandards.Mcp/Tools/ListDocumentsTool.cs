using System.ComponentModel;
using System.Text;
using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// Lists every HexMaster coding standard with the metadata needed to choose one.
/// </summary>
/// <remarks>
/// The discovery half of the surface: <see cref="GetDocumentTool"/> is unusable without it,
/// because an agent has to know an id before it can ask for a document. A thin adapter, like
/// its sibling - it holds no HTTP client, no GitHub knowledge, and no cache.
///
/// <c>[McpServerToolType]</c> is what <c>WithToolsFromAssembly()</c> scans for. Without it
/// the class is invisible to discovery and the tool silently never appears, with nothing at
/// build time to say so.
/// </remarks>
[McpServerToolType]
internal sealed class ListDocumentsTool
{
    private readonly IDocumentService _documents;

    public ListDocumentsTool(IDocumentService documents)
    {
        _documents = documents;
    }

    [McpServerTool(Name = "list_documents")]
    [Description("""
        Lists every HexMaster coding standard with its id, title, category, description, and
        tags. Returns metadata only - no document text.

        Call this first to discover what standards exist and to get the id you need for
        get_document. It takes no arguments and always returns the whole catalog; to narrow
        by keyword, use the search tool instead.
        """)]
    public async Task<CallToolResult> ListDocumentsAsync(CancellationToken cancellationToken)
    {
        // No parameters at all: an agent calling this is orienting itself, and a filter
        // argument would invite it to guess a category, get nothing back, and conclude the
        // catalog is empty. Any argument a client sends anyway is simply not bound.
        var result = await _documents.GetListingAsync(cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            DocumentOutcome.Success => Success(result.Value!),

            // Never an empty list: "no catalog" and "no documents" are different answers, and
            // an agent told the latter stops asking.
            DocumentOutcome.NotReady => Error(
                "The coding standards catalog is not available yet - it has not finished loading from GitHub. Retry in a few seconds."),

            _ => Error(result.Message ?? "The coding standards could not be listed.")
        };
    }

    /// <summary>
    /// Renders the listing as markdown grouped by category, in the order the service
    /// returned - which is category then id, and stable across calls.
    /// </summary>
    private static CallToolResult Success(IReadOnlyList<DocumentListEntry> entries)
    {
        if (entries.Count == 0)
        {
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock
                    {
                        Text = "The coding standards catalog loaded successfully and lists no documents."
                    }
                ]
            };
        }

        var text = new StringBuilder();
        text.Append("# HexMaster coding standards (")
            .Append(entries.Count)
            .AppendLine(entries.Count == 1 ? " document)" : " documents)")
            .AppendLine()
            .AppendLine("Retrieve any of these in full with `get_document`, using its id.");

        DocumentCategory? category = null;

        foreach (var entry in entries)
        {
            if (category != entry.Category)
            {
                category = entry.Category;
                text.AppendLine()
                    .Append("## ")
                    .AppendLine(DocumentCategories.CatalogValueFor(entry.Category));
            }

            text.AppendLine()
                .Append("### ")
                .AppendLine(entry.Title)
                .AppendLine()
                .Append("- **Id**: ")
                .AppendLine(entry.Id)
                .Append("- **Tags**: ")
                .AppendLine(entry.Tags.Count == 0 ? "(none)" : string.Join(", ", entry.Tags))
                .Append("- **Description**: ")
                .AppendLine(entry.Description);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text.ToString() }]
        };
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };
}
