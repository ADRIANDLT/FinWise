using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.Orchestrator;

/// <summary>
/// Infrastructure setup for Azure OpenAI client and logging
/// </summary>
public static class Infrastructure
{
    /// <summary>
    /// Creates and configures Azure OpenAI chat client from environment variables
    /// Required environment variables:
    /// - AZURE_OPENAI_ENDPOINT: Azure OpenAI endpoint URL
    /// - AZURE_OPENAI_DEPLOYMENT: Model deployment name
    /// - AZURE_OPENAI_API_KEY: API key for authentication
    /// </summary>
    public static IChatClient CreateAzureOpenAIChatClient()
    {
        /*
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is required");
        
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
            ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME environment variable is required");
        
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY environment variable is required");
        */

        var endpoint = "https://ai-foundry-cesardl.openai.azure.com/";
        var deploymentName = "gpt-4o-mini-cesardl-model-deployment";
        var apiKey = "2ekgfLpxhhZSe4ffl7SvAatL1LFluh1UeoQ11Q4XNuRVko80xispJQQJ99BEACYeBjFXJ3w3AAAAACOGYlnD";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        var chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

        Log.Information("Azure OpenAI client configured with endpoint: {Endpoint}, deployment: {Deployment}", 
            endpoint, deploymentName);

        return chatClient;
    }

    /// <summary>
    /// Configures Serilog structured logging to console with JSON output
    /// </summary>
    public static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();

        Log.Information("Serilog logging configured");
    }

    /// <summary>
    /// Handles exceptions for the MCP server with structured logging
    /// </summary>
    public static void HandleMcpServerException(Exception ex, string context)
    {
        Log.Error(ex, "MCP Server error in context: {Context}", context);
        
        // Log additional details for specific exception types
        if (ex is InvalidOperationException)
        {
            Log.Warning("Configuration or state issue detected - check environment variables and setup");
        }
        else if (ex is TimeoutException)
        {
            Log.Warning("Operation timed out - consider increasing timeout threshold");
        }
        else if (ex is HttpRequestException)
        {
            Log.Warning("Network or API communication issue - check Azure OpenAI endpoint accessibility");
        }
    }
}
