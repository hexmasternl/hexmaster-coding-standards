using System.ComponentModel;
using System.Text;
using HexMaster.CodingStandards.Docs.Catalog;
using HexMaster.CodingStandards.Docs.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexMaster.CodingStandards.Mcp.Tools;

/// <summary>
/// Finds the HexMaster coding standards carrying a subject tag.
/// </summary>
/// <remarks>
/// The narrowing half of the surface: <c>list_documents</c> answers "what exists", this
/// answers "what is about X", and <see cref="GetDocumentTool"/> answers "give me that
/// one". A thin adapter like both of them - it normalises nothing, compares nothing, and
/// sorts nothing. Every matching rule lives in the Docs project, where it is tested without
/// a host.
/// </remarks>
[McpServerToolType]
internal sealed class FindDocumentsByTagTool
{
    private readonly IDocumentService _documents;

    public FindDocumentsByTagTool(IDocumentService documents)
    {
        _documents = documents;
    }

    [McpServerTool(Name = "find_documents_by_tag")]
    [Description("""
        Finds the HexMaster coding standards carrying one subject tag, returning each match's
        id, title, category, and description. Returns metadata only - no document text, and
        not the documents' other tags.

        Takes exactly one tag. Documents carrying it are returned; if none does, documents
        with a tag containing it are returned instead and the response says the match was
        approximate. Use list_documents to see the tags actually in use, and get_document to
        read a match in full.
        """)]
    public async Task<CallToolResult> FindDocumentsByTagAsync(
        [Description("A single subject tag, for example 'caching'. Case and surrounding whitespace do not matter.")]
        string tag,
        CancellationToken cancellationToken)
    {
        // Rejected before the catalog is touched: a blank tag can only be a caller bug, and
        // answering it with every document would look like a successful narrowing.
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Error("A tag is required. Use list_documents to see which tags the catalog uses.");
        }

        var result = await _documents.FindByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        // Failures come back as tool results flagged isError rather than protocol errors, so
        // the model sees them and can react - the same contract the sibling tools keep.
        return result.Outcome switch
        {
            DocumentOutcome.Success => Success(result.Value!),

            // Never an empty list: "nothing is tagged that way" and "I cannot tell you what
            // is tagged" are different answers, and an agent told the former stops asking.
            DocumentOutcome.NotReady => Error(
                "The coding standards catalog is not available yet - it has not finished loading from GitHub. Retry in a few seconds."),

            _ => Error(result.Message ?? $"The coding standards could not be searched for the tag '{tag}'.")
        };
    }

    /// <summary>
    /// Renders the matches as markdown, opening with whether they carry the tag or merely
    /// resemble it.
    /// </summary>
    /// <remarks>
    /// The exact-versus-approximate distinction is stated once, at the top, rather than as a
    /// field on every entry: match quality is a property of the selection, and an agent that
    /// cannot see it will conclude the tag it guessed is one the catalog really uses.
    /// </remarks>
    private static CallToolResult Success(TagSelection selection)
    {
        if (selection.Documents.Count == 0)
        {
            return Text(
                $"No coding standard is tagged '{selection.Tag}', and none carries a tag containing it. Use list_documents to see which tags the catalog uses.");
        }

        var text = new StringBuilder();

        if (selection.Match == TagMatchKind.Fallback)
        {
            text.Append("No coding standard is tagged '")
                .Append(selection.Tag)
                .AppendLine("'. These are approximate matches - documents carrying a tag that contains it.")
                .AppendLine()
                .AppendLine("Use `list_documents` to see the tags the catalog actually uses.");
        }
        else
        {
            text.Append("Coding standards tagged '")
                .Append(selection.Tag)
                .AppendLine("':");
        }

        text.AppendLine()
            .AppendLine("Retrieve any of these in full with `get_document`, using its id.");

        // The service returns category-then-id order, so grouping is a single pass.
        DocumentCategory? category = null;

        foreach (var document in selection.Documents)
        {
            if (category != document.Category)
            {
                category = document.Category;
                text.AppendLine()
                    .Append("## ")
                    .AppendLine(DocumentCategories.CatalogValueFor(document.Category));
            }

            text.AppendLine()
                .Append("### ")
                .AppendLine(document.Title)
                .AppendLine()
                .Append("- **Id**: ")
                .AppendLine(document.Id)
                .Append("- **Description**: ")
                .AppendLine(document.Description);
        }

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
