using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.Identity;
using FinWise.McpServer;
using FinWise.MultiAgentWorkflow.Agents.StockSpecializedAgent;
using Microsoft.Agents.AI.Hosting;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore.CosmosDb;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore.InMemory;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

Infrastructure.ConfigureLogging();

try
{
    Log.Information("Starting FinWise MCP Server");

    // Initialize Azure OpenAI client
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();

    // Load configuration
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    // Configure CosmosDB options
    var cosmosDbOptions = new CosmosDbOptions();
    configuration.GetSection(CosmosDbOptions.SectionName).Bind(cosmosDbOptions);

    // Create profile store based on configuration
    IUserProfileStore profileStore;
    if (cosmosDbOptions.Enabled)
    {
        Log.Information("Using CosmosDB profile store (Endpoint: {Endpoint}, Database: {Database})",
            cosmosDbOptions.Endpoint, cosmosDbOptions.DatabaseName);

        var cosmosClientOptions = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }
        };

        if (cosmosDbOptions.AllowInsecureTls)
        {
            Log.Warning("CosmosDB TLS validation disabled - for development use only");
            cosmosClientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
        }

        var cosmosClient = new CosmosClient(cosmosDbOptions.Endpoint, cosmosDbOptions.Key, cosmosClientOptions);
        profileStore = new CosmosDbUserProfileStore(cosmosClient, Options.Create(cosmosDbOptions));
    }
    else
    {
        Log.Information("Using in-memory profile store");
        profileStore = new InMemoryUserProfileStore();
    }

    AgentSessionStore sessionStore = new InMemoryAgentSessionStore();

    // Resolve the stock specialized agent from Azure AI Foundry
    var stockAgentEndpoint = Environment.GetEnvironmentVariable("STOCK_AGENT_PROJECT_ENDPOINT")
        ?? throw new InvalidOperationException("Environment variable 'STOCK_AGENT_PROJECT_ENDPOINT' is required.");
    var stockAgentName = Environment.GetEnvironmentVariable("STOCK_AGENT_NAME")
        ?? throw new InvalidOperationException("Environment variable 'STOCK_AGENT_NAME' is required.");
    var stockAgentTenantId = Environment.GetEnvironmentVariable("FINWISE_AZURE_TENANT_ID")
        ?? throw new InvalidOperationException("Environment variable 'FINWISE_AZURE_TENANT_ID' is required.");
    var stockAgentClientId = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_ID")
        ?? throw new InvalidOperationException("Environment variable 'FINWISE_AZURE_CLIENT_ID' is required.");
    var stockAgentClientSecret = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_SECRET")
        ?? throw new InvalidOperationException("Environment variable 'FINWISE_AZURE_CLIENT_SECRET' is required.");

    var stockAgentFactory = new StockSpecializedAgentFactory(
        new AIProjectClient(new Uri(stockAgentEndpoint), new ClientSecretCredential(stockAgentTenantId, stockAgentClientId, stockAgentClientSecret)),
        stockAgentName);
    var stockAgent = await stockAgentFactory.CreateAgentAsync();

    var workflowService = new FinWiseWorkflowService(chatClient, profileStore, sessionStore, stockAgent);

    // Build the web application
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.AddSerilog();

    builder.Services.AddSingleton(workflowService);
    builder.Services.AddSingleton(new McpSessionMapping());
    builder.Services.AddHttpContextAccessor();

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "FinWise MCP Server",
                Version = "1.0.0"
            };
        })
        .WithHttpTransport(httpOptions =>
        {
            httpOptions.Stateless = false;
            httpOptions.IdleTimeout = TimeSpan.FromMinutes(30);
        })
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp("/mcp");

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Urls;
        if (addresses.Count > 0)
        {
            Log.Information("FinWise MCP Server ready - listening on {Urls}",
                string.Join(", ", addresses.Select(u => $"{u}/mcp")));
        }
        else
        {
            Log.Information("FinWise MCP Server ready");
        }
    });
    await app.RunAsync();
}
catch (Exception ex)
{
    Infrastructure.HandleMcpServerException(ex, "Startup");
    Log.Fatal(ex, "Fatal error during MCP Server startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
