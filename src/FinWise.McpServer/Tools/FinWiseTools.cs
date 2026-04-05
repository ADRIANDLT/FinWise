using System.ComponentModel;
using FinWise.McpServer.Infrastructure.McpSession;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.AspNetCore.Http;
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
}
