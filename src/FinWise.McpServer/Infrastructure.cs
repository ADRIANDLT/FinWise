using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.McpServer;

/// <summary>
/// Infrastructure setup for Azure OpenAI client and logging
/// </summary>
public static class Infrastructure
{
    /// <summary>
    /// Creates and configures Azure OpenAI chat client from environment variables
    /// Required environment variables:
    /// - AZURE_OPENAI_ENDPOINT: Azure OpenAI endpoint URL
    /// - AZURE_OPENAI_DEPLOYMENT_NAME: Model deployment name
    /// - AZURE_OPENAI_API_KEY: API key for authentication
    /// </summary>
    public static IChatClient CreateAzureOpenAIChatClient()
    {
        Log.Information("Attempting to load Azure OpenAI configuration from environment variables...");
        
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        // Log what was found (without exposing sensitive data)
        Log.Information("AZURE_OPENAI_ENDPOINT: {Status}", string.IsNullOrEmpty(endpoint) ? "NOT SET" : "SET");
        Log.Information("AZURE_OPENAI_DEPLOYMENT_NAME: {Status}", string.IsNullOrEmpty(deploymentName) ? "NOT SET" : "SET");
        Log.Information("AZURE_OPENAI_API_KEY: {Status}", string.IsNullOrEmpty(apiKey) ? "NOT SET" : "SET");

        // Validate that all required environment variables are set
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

    /// <summary>
    /// Configures Serilog structured logging to console and file
    /// </summary>
    public static void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinWise-Orchestrator-MCP", "Logs", "finwise-.log");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}]{RequestId: [Req:lj]} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {RequestId:l}{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Serilog logging configured. Log file: {LogPath}", logPath);
    }

    /// <summary>
    /// Handles exceptions for the MCP server with structured logging
    /// </summary>
    public static void HandleMcpServerException(Exception ex, string context)
    {
        Log.Error(ex, "MCP Server error in context: {Context}", context);
        
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
