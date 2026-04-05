using FinWise.McpServer.Infrastructure.AgentSessionStorage;
using FinWise.McpServer.Infrastructure.AzureAIFoundry;
using FinWise.McpServer.Infrastructure.AzureOpenAI;
using FinWise.McpServer.Infrastructure.Logging;
using FinWise.McpServer.Infrastructure.McpSession.Redis;
using FinWise.McpServer.Infrastructure.UserProfileStorage;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ModelContextProtocol.AspNetCore;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

LoggingSetup.ConfigureLogging();

try
{
    Log.Information("Starting FinWise MCP Server");

    // Initialize external dependencies via infrastructure factories
    var chatClient = AzureOpenAIChatClientFactory.CreateChatClient();

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var profileStore = UserProfileStoreFactory.CreateProfileStore(configuration);
    var (sessionStore, redis, redisOptions) = await AgentSessionStoreFactory.CreateSessionStoreAsync(configuration);
    var stockAgent = await StockAgentFactory.TryCreateStockAgentAsync();

    var workflowService = new FinWiseWorkflowService(chatClient, profileStore, sessionStore, stockAgent);

    // Build the web application
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.AddSerilog();

    builder.Services.AddSingleton(workflowService);
    builder.Services.AddHttpContextAccessor();

    if (redis is not null)
    {
        // MCP session migration: stores MCP initialize handshake params in Redis (mcpinit:* keys)
        // so any instance can reconstruct the MCP transport session. Separate from agent session
        // storage (agentsession:* keys) which holds conversation state.
        builder.Services.AddSingleton<ISessionMigrationHandler>(
            new RedisSessionMigrationHandler(redis, TimeSpan.FromMinutes(redisOptions.SessionTtlMinutes)));
    }

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
    app.MapGet("/health", () => "healthy");

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
    LoggingSetup.HandleMcpServerException(ex, "Startup");
    Log.Fatal(ex, "Fatal error during MCP Server startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
