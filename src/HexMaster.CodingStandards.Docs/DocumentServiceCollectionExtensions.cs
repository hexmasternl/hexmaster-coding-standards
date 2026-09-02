using HexMaster.CodingStandards.Docs.Documents;
using HexMaster.CodingStandards.Docs.GitHub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
    /// Registers the document service, its GitHub client, the catalog and body caches, and
    /// the catalog loader, binding options from the <c>Documents</c> configuration section.
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

        // Catches what data annotations cannot express, with a message naming the setting.
        services.AddSingleton<IValidateOptions<GitHubContentOptions>, GitHubContentOptionsValidator>();

        services.TryAddTimeProvider();

        services.AddHttpClient<IGitHubContentClient, GitHubContentClient>(
                GitHubContentClient.HttpClientName,
                (serviceProvider, client) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<IOptions<GitHubContentOptions>>()
                        .Value;

                    client.Timeout = options.RequestTimeout;

                    // GitHub rejects API requests with no user agent.
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("hexmaster-coding-standards-mcp");
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                })
            .AddStandardResilienceHandler();

        services.AddSingleton<DocumentSetCache>();
        services.AddSingleton<DocumentBodyCache>();
        services.AddSingleton<IDocumentService, DocumentService>();
        // Registered twice on purpose, resolving to one instance: the hosted service does the
        // eager startup load, and DocumentService holds the same loader to refresh a catalog
        // that has aged out on read.
        services.AddSingleton<CatalogLoader>();
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<CatalogLoader>());

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
/// Reports whether the server has a catalog to serve. Registered by the host, which owns the
/// health-checks pipeline; the check itself only needs the catalog cache.
/// </summary>
/// <remarks>
/// Keyed off the catalog alone, deliberately. A body that cannot be fetched degrades one
/// document; it must not take a replica out of rotation.
/// </remarks>
public sealed class DocumentsHealthCheck : IHealthCheck
{
    private readonly DocumentSetCache _cache;

    /// <summary>Creates the check over the catalog cache.</summary>
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
                "No coding standards catalog has been loaded from GitHub yet.")
            : HealthCheckResult.Healthy(
                $"Catalog of {set.Count} document(s) loaded at {set.LoadedAt:o}."));
    }
}
