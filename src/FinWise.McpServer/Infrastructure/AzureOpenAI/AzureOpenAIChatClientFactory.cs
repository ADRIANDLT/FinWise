using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.McpServer.Infrastructure.AzureOpenAI;

/// <summary>
/// Creates and configures the Azure OpenAI <see cref="IChatClient"/> from environment variables.
/// </summary>
public static class AzureOpenAIChatClientFactory
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> backed by Azure OpenAI.
    /// Required environment variables:
    /// <list type="bullet">
    ///   <item><c>AZURE_OPENAI_ENDPOINT</c> — Azure OpenAI endpoint URL</item>
    ///   <item><c>AZURE_OPENAI_DEPLOYMENT_NAME</c> — Model deployment name</item>
    ///   <item><c>AZURE_OPENAI_API_KEY</c> — API key for authentication</item>
    /// </list>
    /// </summary>
    public static IChatClient CreateChatClient()
    {
        Log.Information("Attempting to load Azure OpenAI configuration from environment variables...");

        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        Log.Information("AZURE_OPENAI_ENDPOINT: {Status}", string.IsNullOrEmpty(endpoint) ? "NOT SET" : "SET");
        Log.Information("AZURE_OPENAI_DEPLOYMENT_NAME: {Status}", string.IsNullOrEmpty(deploymentName) ? "NOT SET" : "SET");
        Log.Information("AZURE_OPENAI_API_KEY: {Status}", string.IsNullOrEmpty(apiKey) ? "NOT SET" : "SET");

        if (string.IsNullOrEmpty(endpoint))
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is required");

        if (string.IsNullOrEmpty(deploymentName))
            throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME environment variable is required");

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("AZURE_OPENAI_API_KEY environment variable is required");

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        var chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

        Log.Information("Azure OpenAI client configured with endpoint: {Endpoint}, deployment: {Deployment}",
            endpoint, deploymentName);

        return chatClient;
    }
}
