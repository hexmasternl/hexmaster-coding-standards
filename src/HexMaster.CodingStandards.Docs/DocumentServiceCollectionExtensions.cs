using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HexMaster.CodingStandards.Docs;

/// <summary>
/// Wires the document layer into a host.
/// </summary>
/// <remarks>
/// One call registers everything the Docs project needs, so adding a dependency here never
/// requires an edit to the host's composition. See the <c>mcp-server-host</c> spec, "The
/// document service is registered for dependency injection".
/// </remarks>
public static class DocumentServiceCollectionExtensions
{
    /// <summary>The health check name reported for document readiness.</summary>
    public const string HealthCheckName = "documents";

    /// <summary>
    /// Registers the document service, its GitHub archive source, and the background
    /// refresh, binding options from the <c>Documents</c> configuration section.
    /// </summary>
    public static IServiceCollection AddCodingStandardsDocuments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<GitHubContentOptions>()
            .Bind(configuration.GetSection(GitHubContentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();

        services.AddHttpClient<IContentArchiveSource, GitHubContentArchiveSource>(
                GitHubContentArchiveSource.HttpClientName,
                (serviceProvider, client) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubContentOptions>>()
                        .Value;

                    client.Timeout = options.RequestTimeout;

                    // GitHub rejects API requests with no user agent.
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("hexmaster-coding-standards-mcp");
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                })
            .AddStandardResilienceHandler();

        services.AddSingleton<DocumentSetCache>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<ContentRefreshService>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<ContentRefreshService>());

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

/// <summary>
/// Reports whether the server has content to serve. Registered by the host, which owns the
/// health-checks pipeline; the check itself only needs the document cache.
/// </summary>
public sealed class DocumentsHealthCheck : IHealthCheck
{
    private readonly DocumentSetCache _cache;

    /// <summary>Creates the check over the content cache.</summary>
    public DocumentsHealthCheck(DocumentSetCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var set = _cache.Current;

        return Task.FromResult(set is null
            ? HealthCheckResult.Unhealthy(
                "No coding standards have been loaded from GitHub yet.")
            : HealthCheckResult.Healthy(
                $"{set.Count} document(s) loaded at {set.LoadedAt:o}."));
    }
}
