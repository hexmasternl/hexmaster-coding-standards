using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexMaster.CodingStandards.Docs.Catalog;

/// <summary>
/// Thrown when <c>docs/index.json</c> cannot be parsed at all. Unparseable JSON fails the
/// whole load - there is nothing to salvage - whereas an individual bad entry is skipped and
/// reported. See the <c>document-service</c> spec, "Malformed catalog content is reported,
/// not silently tolerated".
/// </summary>
public sealed class CatalogFormatException : Exception
{
    /// <summary>Creates the exception with a message and the underlying parse failure.</summary>
    public CatalogFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The outcome of parsing a catalog: the entries that were valid, and every problem found
/// along the way. The runtime serves <see cref="Catalog"/> and logs
/// <see cref="Problems"/>; CI treats any problem as a failure.
/// </summary>
/// <param name="Catalog">The valid entries.</param>
/// <param name="Problems">Every entry-level problem found while parsing.</param>
public sealed record CatalogParseResult(DocumentCatalog Catalog, IReadOnlyList<CatalogProblem> Problems);

/// <summary>
/// Reads <c>docs/index.json</c> into a <see cref="DocumentCatalog"/>.
/// </summary>
/// <remarks>
/// Entries are deserialized as raw strings first and converted afterwards, rather than
/// letting the JSON serializer bind straight to enums. That is deliberate: a serializer that
/// meets an unknown <c>category</c> throws and takes the whole file down with it, whereas
/// this needs to skip the one bad entry and keep serving the rest.
/// </remarks>
public static class CatalogParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };

    /// <summary>
    /// Parses catalog JSON, collecting per-entry problems rather than failing on them.
    /// </summary>
    /// <exception cref="CatalogFormatException">The JSON could not be parsed, or has no <c>documents</c> array.</exception>
    public static CatalogParseResult Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        CatalogFile? file;
        try
        {
            file = JsonSerializer.Deserialize<CatalogFile>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new CatalogFormatException(
                $"docs/index.json is not valid JSON: {exception.Message}", exception);
        }

        if (file?.Documents is null)
        {
            throw new CatalogFormatException(
                "docs/index.json has no 'documents' array.");
        }

        var problems = new List<CatalogProblem>();
        var entries = new List<CatalogEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < file.Documents.Count; index++)
        {
            var entry = Convert(file.Documents[index], index, problems);
            if (entry is null)
            {
                continue;
            }

            if (!seenIds.Add(entry.Id))
            {
                // Skipping rather than overwriting: a duplicate id shadowing an earlier entry
                // would silently change which document an id resolves to.
                problems.Add(new CatalogProblem(
                    CatalogProblemKind.DuplicateId,
                    $"Entry '{entry.Id}' ({entry.Path}) duplicates an id already in the catalog; the duplicate was skipped.",
                    entry.Id,
                    entry.Path));
                continue;
            }

            entries.Add(entry);
        }

        return new CatalogParseResult(new DocumentCatalog(entries), problems);
    }

    private static CatalogEntry? Convert(CatalogEntryFile raw, int index, List<CatalogProblem> problems)
    {
        // Without an id there is nothing to name the entry by in any later problem, so it is
        // the one failure that has to be reported positionally.
        if (string.IsNullOrWhiteSpace(raw.Id))
        {
            problems.Add(new CatalogProblem(
                CatalogProblemKind.MissingProperty,
                $"The entry at position {index} has no 'id'."));
            return null;
        }

        var id = raw.Id;
        var valid = true;

        if (string.IsNullOrWhiteSpace(raw.Title))
        {
            problems.Add(Missing(id, "title"));
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(raw.Description))
        {
            problems.Add(Missing(id, "description"));
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(raw.Path))
        {
            problems.Add(Missing(id, "path"));
            valid = false;
        }

        if (!DocumentCategories.TryParse(raw.Category, out var category))
        {
            problems.Add(new CatalogProblem(
                raw.Category is null ? CatalogProblemKind.MissingProperty : CatalogProblemKind.UnknownCategory,
                $"Entry '{id}' declares category '{raw.Category ?? "(none)"}'; allowed values are {Join(DocumentCategories.AllowedCatalogValues)}.",
                id,
                raw.Path));
            valid = false;
        }

        if (!DocumentStatuses.TryParse(raw.Status, out var status))
        {
            problems.Add(new CatalogProblem(
                raw.Status is null ? CatalogProblemKind.MissingProperty : CatalogProblemKind.UnknownStatus,
                $"Entry '{id}' declares status '{raw.Status ?? "(none)"}'; allowed values are {Join(DocumentStatuses.AllowedCatalogValues)}.",
                id,
                raw.Path));
            valid = false;
        }

        if (raw.Tags is null)
        {
            problems.Add(Missing(id, "tags"));
            valid = false;
        }

        return valid
            ? new CatalogEntry(id, raw.Title!, raw.Description!, category, status, raw.Tags!, raw.Path!)
            : null;
    }

    private static CatalogProblem Missing(string id, string property) =>
        new(CatalogProblemKind.MissingProperty, $"Entry '{id}' has no '{property}'.", id);

    private static string Join(IReadOnlyList<string> values) =>
        string.Join(", ", values.Select(value => $"'{value}'"));

    private sealed class CatalogFile
    {
        [JsonPropertyName("documents")]
        public IReadOnlyList<CatalogEntryFile>? Documents { get; init; }
    }

    private sealed class CatalogEntryFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("tags")]
        public IReadOnlyList<string>? Tags { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }
    }
}
