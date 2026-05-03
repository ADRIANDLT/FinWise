using System.ComponentModel;
using FinWise.McpServer.Infrastructure.McpSession;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Serilog;

namespace FinWise.McpServer.Tools;

[McpServerToolType]
public static class FinWiseTools
{
    [McpServerTool(Name = "run_finwise_workflow")]
    [Description("Send user messages to FinWise financial advisor. CRITICAL: The 'query' parameter is the user's EXACT message - pass it verbatim without asking for userId, email, or any other information first. DO NOT ask the user for additional details before calling this tool. Just call it immediately with whatever the user said. The FinWise system internally manages all user identification and profile collection through conversation. IMPORTANT: On a NEW session, the FinWise system WILL ask for the user's email address as the first step - this is expected behavior. You MUST relay the email request back to the user and then pass their email response to this tool.")]
    public static async Task<string> RunFinWiseWorkflow(
        IServiceProvider serviceProvider,
        [Description("The user's exact message")] string query,
        CancellationToken cancellationToken = default)
    {
        var workflowService = serviceProvider.GetRequiredService<FinWiseWorkflowService>();
        var httpContext = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("HTTP context unavailable for MCP request.");

        string mcpSessionId;
        try
        {
            mcpSessionId = McpSessionAccessor.GetSessionId(httpContext);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "MCP session header missing");
            return "Unable to continue because the MCP client did not provide an MCP-Session-Id header. Please restart the chat from a streamable HTTP client.";
        }

        var result = await workflowService.ProcessMessageAsync(mcpSessionId, query);

        return result.Response;
    }

    [McpServerTool(Name = "reset_conversation")]
    [Description("Clear conversation history to start fresh. User profiles are retained.")]
    public static async Task<string> ResetSession(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var httpContext = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("HTTP context unavailable for MCP request.");
        var workflowService = serviceProvider.GetRequiredService<FinWiseWorkflowService>();

        string mcpSessionId;
        try
        {
            mcpSessionId = McpSessionAccessor.GetSessionId(httpContext);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "MCP session header missing");
            return "No active session to reset.";
        }

        await workflowService.ResetSessionAsync(mcpSessionId);

        return "Conversation history cleared. User profiles are retained in the store.";
    }

    [McpServerTool(Name = "get_storage_info")]
    [Description("Explains where FinWise stores data — which data concerns use in-memory structures and which use external databases (e.g., CosmosDB, Redis). Call when the user asks where their data is stored or what databases FinWise uses.")]
    public static Task<string> GetStorageInfo(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var forceInMemory = configuration.GetValue<bool>("ForceInMemoryData");
        if (Environment.GetEnvironmentVariable("FINWISE_FORCE_IN_MEMORY_DATA") is { Length: > 0 } forceEnv)
            forceInMemory = string.Equals(forceEnv, "true", StringComparison.OrdinalIgnoreCase);

        if (forceInMemory)
        {
            return Task.FromResult(
                """
                FinWise Storage Configuration (all storage forced to in-memory — ForceInMemoryData=true):
                • User Profiles: In-memory data store (ConcurrentDictionary)
                • Agent Sessions (conversation state): In-memory data store (ConcurrentDictionary)
                • MCP Session Migration: Disabled (only needed for multi-instance scale-out with Redis)
                """);
        }

        var redisEnabled = configuration.GetValue<bool>("Redis:Enabled");
        if (Environment.GetEnvironmentVariable("FINWISE_REDIS_ENABLED") is { Length: > 0 } redisEnv)
            redisEnabled = string.Equals(redisEnv, "true", StringComparison.OrdinalIgnoreCase);

        var cosmosDbEnabled = configuration.GetValue<bool>("CosmosDb:Enabled");
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ENABLED") is { Length: > 0 } cosmosEnv)
            cosmosDbEnabled = string.Equals(cosmosEnv, "true", StringComparison.OrdinalIgnoreCase);

        var userProfiles = cosmosDbEnabled
            ? "Azure CosmosDB (NoSQL document database)"
            : "In-memory data store (ConcurrentDictionary)";

        var agentSessions = redisEnabled
            ? "Redis (fast key-value external persistent store)"
            : "In-memory data store (ConcurrentDictionary)";

        var mcpMigration = redisEnabled
            ? "Redis (fast key-value external persistent store)"
            : "Disabled (only needed for multi-instance scale-out with Redis)";

        return Task.FromResult(
            $"""
            FinWise Storage Configuration:
            • User Profiles: {userProfiles}
            • Agent Sessions (conversation state): {agentSessions}
            • MCP Session Migration: {mcpMigration}
            """);
    }
}
