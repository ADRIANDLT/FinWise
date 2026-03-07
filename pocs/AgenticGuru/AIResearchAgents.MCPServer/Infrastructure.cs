using Serilog;
using Serilog.Events;

namespace AIResearchAgents.MCPServer;

public static class Infrastructure
{
    public static void ConfigureLogging(WebApplicationBuilder builder)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIResearchAgents-MCPServer",
            "Logs",
            "log-.txt"
        );

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        builder.Host.UseSerilog();
    }

    public static void HandleMcpServerException(Exception ex, Microsoft.Extensions.Logging.ILogger logger)
    {
        switch (ex)
        {
            case InvalidOperationException when ex.Message.Contains("environment variable"):
                logger.LogWarning("Configuration error: {Message}", ex.Message);
                break;

            case TaskCanceledException:
            case OperationCanceledException:
                logger.LogWarning("Request was cancelled");
                break;

            case HttpRequestException:
                logger.LogWarning("Network error communicating with Azure: {Message}", ex.Message);
                break;

            default:
                logger.LogError(ex, "Unexpected error during MCP tool execution");
                break;
        }
    }
}
