using Azure.AI.Projects;
using Azure.Identity;
using FinWise.MultiAgentWorkflow.Agents.StockSpecializedAgent;
using Microsoft.Agents.AI;
using Serilog;

namespace FinWise.McpServer.Infrastructure.AzureAIFoundry;

/// <summary>
/// Creates the stock specialized <see cref="AIAgent"/> from Azure AI Foundry using environment variables.
/// </summary>
public static class StockAgentFactory
{
    /// <summary>
    /// Attempts to resolve a stock specialized agent from Azure AI Foundry.
    /// Returns <c>null</c> if any required environment variable is missing — the workflow
    /// will run without stock analysis capabilities.
    /// </summary>
    public static async Task<AIAgent?> TryCreateStockAgentAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("STOCK_AGENT_PROJECT_ENDPOINT");
        var agentName = Environment.GetEnvironmentVariable("STOCK_AGENT_NAME");
        var tenantId = Environment.GetEnvironmentVariable("FINWISE_AZURE_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_SECRET");

        // Log env var status (without exposing secrets) — matches AzureOpenAIChatClientFactory pattern
        Log.Information("STOCK_AGENT_PROJECT_ENDPOINT: {Status}", string.IsNullOrEmpty(endpoint) ? "NOT SET" : "SET");
        Log.Information("STOCK_AGENT_NAME: {Status}", string.IsNullOrEmpty(agentName) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_TENANT_ID: {Status}", string.IsNullOrEmpty(tenantId) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_CLIENT_ID: {Status}", string.IsNullOrEmpty(clientId) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_CLIENT_SECRET: {Status}", string.IsNullOrEmpty(clientSecret) ? "NOT SET" : "SET");

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(agentName) ||
            string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Log.Warning("Stock agent disabled — one or more required environment variables are missing");
            return null;
        }

        var projectClient = new AIProjectClient(
            new Uri(endpoint),
            new ClientSecretCredential(tenantId, clientId, clientSecret));

        var factory = new StockSpecializedAgentFactory(projectClient, agentName);
        return await factory.CreateAgentAsync();
    }
}
