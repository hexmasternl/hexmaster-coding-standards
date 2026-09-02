using System.ComponentModel.DataAnnotations;

namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// Where the served documents come from and how long they are held.
/// </summary>
/// <remarks>
/// Defaults target the public repository, so the server runs with no configuration at all.
/// Every value is bindable from configuration, which means environment variables override
/// them in the container.
///
/// Catalog and bodies are cached separately but for the same default duration, so the
/// freshness guarantee is one sentence: nothing served is more than 30 minutes old. They
/// remain two settings because they expire on different clocks - the catalog from when it
/// was loaded, a body from when that body was fetched. See the
/// <c>docs-serve-document-list</c> design.
/// </remarks>
public sealed class GitHubContentOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Documents";

    /// <summary>GitHub account or organisation owning the content repository.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Owner { get; set; } = "hexmasternl";

    /// <summary>Content repository name.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Repository { get; set; } = "hexmaster-coding-standards";

    /// <summary>Branch, tag, or commit to serve content from. Applies to catalog and bodies alike.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Ref { get; set; } = "main";

    /// <summary>
    /// How long a loaded catalog is served before the next request re-fetches it. Expiry is
    /// evaluated on read rather than on a timer, so an idle replica issues no requests.
    /// </summary>
    public TimeSpan CatalogCacheLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a fetched document body is served from memory. Expiry is absolute from the
    /// fetch instant, so repeated reads never extend it.
    /// </summary>
    public TimeSpan BodyCacheLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a single request may take before it is abandoned.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional access token. Not needed for a public repository, but bodies are now fetched
    /// one request at a time, so the anonymous limit of 60 requests per hour per IP is
    /// reachable; a token raises it to 5,000. Never logged.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Repository-relative path of the catalog, fetched in full on every refresh.
    /// </summary>
    public const string CatalogPath = "docs/index.json";

    /// <summary>
    /// The GitHub Contents API URI for a repository path at the configured ref.
    /// </summary>
    /// <remarks>
    /// The Contents API is used rather than <c>raw.githubusercontent.com</c> because it
    /// honours the same access token as every other call, which is what gives a
    /// rate-limited or private-fork deployment any recourse at all. Callers pass a path that
    /// has already been validated and segment-encoded.
    /// </remarks>
    public Uri ContentUri(string encodedPath) => new(
        $"https://api.github.com/repos/{Owner}/{Repository}/contents/{encodedPath}?ref={Uri.EscapeDataString(Ref)}");
}

/// <summary>
/// Validates the content options beyond what data annotations express, so a misconfiguration
/// fails at startup with a message naming the setting rather than misbehaving later.
/// </summary>
internal sealed class GitHubContentOptionsValidator : Microsoft.Extensions.Options.IValidateOptions<GitHubContentOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, GitHubContentOptions options)
    {
        var failures = new List<string>();

        if (options.BodyCacheLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"{GitHubContentOptions.SectionName}:{nameof(GitHubContentOptions.BodyCacheLifetime)} must be greater than zero; it was '{options.BodyCacheLifetime}'.");
        }

        if (options.CatalogCacheLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"{GitHubContentOptions.SectionName}:{nameof(GitHubContentOptions.CatalogCacheLifetime)} must be greater than zero; it was '{options.CatalogCacheLifetime}'.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{GitHubContentOptions.SectionName}:{nameof(GitHubContentOptions.RequestTimeout)} must be greater than zero; it was '{options.RequestTimeout}'.");
        }

        // Note what is absent: the access token is never mentioned in a failure message.
        return failures.Count == 0
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Success
            : Microsoft.Extensions.Options.ValidateOptionsResult.Fail(failures);
    }
}
