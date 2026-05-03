using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.McpServer.Infrastructure.AzureAIFoundry;

/// <summary>
/// Creates and configures the Azure AI Foundry-backed <see cref="IChatClient"/> from environment
/// variables, using the Foundry <c>AIProjectClient</c> + OpenAI Responses API chain and a
/// service-principal (client secret) credential.
/// </summary>
public static class AzureAIFoundryChatClientFactory
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> backed by Azure AI Foundry's project-scoped
    /// OpenAI Responses API.
    /// Required environment variables:
    /// <list type="bullet">
    ///   <item><c>FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT</c> — Foundry project endpoint URL</item>
    ///   <item><c>FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME</c> — Model deployment name</item>
    ///   <item><c>FINWISE_AZURE_TENANT_ID</c> — Service principal tenant ID</item>
    ///   <item><c>FINWISE_AZURE_CLIENT_ID</c> — Service principal client ID</item>
    ///   <item><c>FINWISE_AZURE_CLIENT_SECRET</c> — Service principal client secret</item>
    /// </list>
    /// </summary>
    public static IChatClient CreateChatClient()
    {
        Log.Information("Attempting to load Azure AI Foundry LLM configuration from environment variables...");

        var projectEndpoint = Environment.GetEnvironmentVariable("FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT");
        var deploymentName = Environment.GetEnvironmentVariable("FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME");
        var tenantId = Environment.GetEnvironmentVariable("FINWISE_AZURE_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_SECRET");

        // Log env var status (never the values themselves) — matches StockAgentFactory pattern
        Log.Information("FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT: {Status}", string.IsNullOrEmpty(projectEndpoint) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME: {Status}", string.IsNullOrEmpty(deploymentName) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_TENANT_ID: {Status}", string.IsNullOrEmpty(tenantId) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_CLIENT_ID: {Status}", string.IsNullOrEmpty(clientId) ? "NOT SET" : "SET");
        Log.Information("FINWISE_AZURE_CLIENT_SECRET: {Status}", string.IsNullOrEmpty(clientSecret) ? "NOT SET" : "SET");

        if (string.IsNullOrEmpty(projectEndpoint))
            throw new InvalidOperationException("FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT environment variable is required");

        if (string.IsNullOrEmpty(deploymentName))
            throw new InvalidOperationException("FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME environment variable is required");

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("FINWISE_AZURE_TENANT_ID environment variable is required");

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("FINWISE_AZURE_CLIENT_ID environment variable is required");

        if (string.IsNullOrEmpty(clientSecret))
            throw new InvalidOperationException("FINWISE_AZURE_CLIENT_SECRET environment variable is required");

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);

#pragma warning disable OPENAI001 // Experimental: AsIChatClient(ResponsesClient, string?) — AIOpenAIResponses → "OPENAI001"
        IChatClient chatClient = projectClient
            .GetProjectOpenAIClient()
            .GetResponsesClient()
            .AsIChatClient(deploymentName);
#pragma warning restore OPENAI001

        Log.Information("Azure AI Foundry chat client configured with project endpoint: {Endpoint}, deployment: {Deployment}",
            projectEndpoint, deploymentName);

        return chatClient;
    }
}
