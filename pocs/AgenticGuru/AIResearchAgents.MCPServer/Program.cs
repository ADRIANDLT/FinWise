using Azure.Core;
using Azure.Identity;
using AIResearchAgents.Core.Agents.AIToolsResearchAgent;
using AIResearchAgents.MCPServer;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Infrastructure.ConfigureLogging(builder);

// Check if deploy mode is requested via CLI arg or environment variable
var deployAgentRequested = args.Contains("--deploy-agent", StringComparer.OrdinalIgnoreCase) ||
    string.Equals(Environment.GetEnvironmentVariable("DEPLOY_AGENTS"), "true", StringComparison.OrdinalIgnoreCase);

// Register Azure authentication
builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());

// Configure MCP server with HTTP transport and auto-discovery
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

// Deploy new agent version if requested
if (deployAgentRequested)
{
    Log.Information("🚀 Deploy mode: Creating new agent version in Azure Foundry...");
    var credential = app.Services.GetRequiredService<TokenCredential>();
    var runner = AIToolsResearchAgentRunner.CreateFromEnvironment(credential: credential);
    await runner.RunAsync(forceNewVersion: true);
    Log.Information("New agent version deployed to Azure Foundry");
}

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("Agentic Trends Guru MCP Server ready — endpoint: {Endpoint}",
        string.Join(", ", app.Urls.Select(u => $"{u}/mcp"))));

app.Run();

// Make the auto-generated Program class accessible to test projects
public partial class Program { }
