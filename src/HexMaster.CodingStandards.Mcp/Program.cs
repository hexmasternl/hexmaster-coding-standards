using HexMaster.CodingStandards.Docs;
using HexMaster.CodingStandards.Mcp.Tools;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// The document layer: downloads the coding standards from GitHub, caches them, and serves
// retrieval, the index, and keyword search. One call so a new dependency inside the Docs
// project never means an edit here.
builder.Services.AddCodingStandardsDocuments(builder.Configuration);

builder.Services
    .AddHealthChecks()
    .AddCheck<DocumentsHealthCheck>(
        DocumentServiceCollectionExtensions.HealthCheckName,
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

builder.Services
    // The one thing the server says without being asked. Clients send it to the model as a
    // system message on connect, which is what makes the skill bootstrap fire at all - a
    // tool description is only read once a model is already looking for a tool.
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithHttpTransport(options =>
    {
        // Stateless is load-bearing, not a default worth changing. The container app runs
        // minReplicas 0 with HTTP scaling, so consecutive requests from one client can land
        // on different replicas and a replica can vanish between them. Stateless mode means
        // no session affinity is needed - at the cost of server-to-client requests
        // (sampling, elicitation), which this server does not use.
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

// Container Apps ingress terminates TLS and forwards plain HTTP, so UseHttpsRedirection()
// would see an HTTP request and redirect - a loop, or a broken client. HTTPS is enforced at
// the edge instead (ingress allowInsecure: false).

app.MapMcp();

// Unauthenticated, and used by the Container Apps probes. Healthy means content has loaded;
// a later failed refresh keeps serving what is cached and stays healthy.
app.MapHealthChecks("/health");

app.Run();
