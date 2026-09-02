using System.ComponentModel.DataAnnotations;

namespace HexMaster.CodingStandards.Docs.GitHub;

/// <summary>
/// Where the served documents come from and how long they are held.
/// </summary>
/// <remarks>
/// Defaults target the public repository, so the server runs with no configuration at all.
/// Every value is bindable from configuration, which means environment variables override
/// them in the container.
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

    /// <summary>Branch, tag, or commit to serve content from.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Ref { get; set; } = "main";

    /// <summary>
    /// How often the content set is refreshed from GitHub. Content can be this stale; the
    /// trade is against the request volume and the cold-start cost of refreshing.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "24:00:00")]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a single archive download may take before it is abandoned.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional access token. Not needed for a public repository, but it raises GitHub's rate
    /// limits and lets the same code serve a private one. Never logged.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// The archive URL for the configured repository and ref. One request per refresh, so
    /// the catalog and every document body come from the same commit.
    /// </summary>
    /// <remarks>
    /// The API tarball endpoint is used rather than a codeload URL because it resolves a
    /// branch, a tag, or a commit SHA from the same path, while codeload needs the ref kind
    /// baked into the URL (<c>refs/heads/</c> versus <c>refs/tags/</c>).
    /// </remarks>
    public Uri ArchiveUri => new(
        $"https://api.github.com/repos/{Owner}/{Repository}/tarball/{Ref}");
}
