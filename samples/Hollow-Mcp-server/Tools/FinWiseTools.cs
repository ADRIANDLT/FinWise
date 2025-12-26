 using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FinWiseOrchestratorMcp.Tools;

/// <summary>
/// Sample MCP tools for the FinWise orchestrator.
/// These tools can be invoked by MCP clients to perform various financial operations.
/// </summary>
[McpServerToolType]
public class FinWiseTools
{
    [McpServerTool]
    [Description("Generates a random number between the specified minimum and maximum values. Useful for testing MCP connectivity.")]
    public int GetRandomNumber(
        [Description("Minimum value (inclusive)")] int min = 0,
        [Description("Maximum value (exclusive)")] int max = 100)
    {
        return Random.Shared.Next(min, max);
    }

    [McpServerTool]
    [Description("Returns a welcome message for the FinWise orchestrator MCP server.")]
    public string GetWelcomeMessage(
        [Description("Name of the user or system requesting the welcome")] string? name = null)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "Welcome to the FinWise Orchestrator MCP Server!"
            : $"Welcome to the FinWise Orchestrator MCP Server, {name}!";
    }
}
