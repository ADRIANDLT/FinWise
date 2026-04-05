using Serilog;

namespace FinWise.McpServer.Infrastructure.Logging;

/// <summary>
/// Configures Serilog structured logging and provides server-level exception handling.
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Creates and assigns the global Serilog logger with console and daily-rolling file sinks.
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
    /// Handles a server exception by logging the error and emitting a categorized warning
    /// based on the exception type.
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
