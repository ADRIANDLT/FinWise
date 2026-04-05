using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace FinWise.McpServer.E2ETestBase;

/// <summary>
/// Abstract base class for MCP end-to-end tests. Provides HTTP client setup,
/// MCP protocol helpers (initialize, tool calls, SSE parsing), and common
/// test-profile setup utilities.
/// </summary>
public abstract class McpEndToEndTestBase : IDisposable
{
    protected readonly HttpClient HttpClient;
    protected readonly ITestOutputHelper Output;
    protected readonly ILogger Logger;
    protected string SessionId;

    /// <summary>
    /// Base URL for the MCP server. Configurable via FINWISE_MCP_URL env var.
    /// Defaults to http://localhost:5000.
    /// </summary>
    protected string McpBaseUrl { get; }

    protected McpEndToEndTestBase(ITestOutputHelper output)
    {
        Output = output;
        HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // MCP Streamable HTTP requires the client to accept both application/json and text/event-stream
        HttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        SessionId = string.Empty;

        McpBaseUrl = Environment.GetEnvironmentVariable("FINWISE_MCP_URL") ?? "http://localhost:5000";

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new XunitLoggerProvider(output)));
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }

    /// <summary>
    /// Initializes an MCP session with the server and returns the session ID.
    /// Updates the internal SessionId field.
    /// </summary>
    protected async Task<string> InitializeMcpSession()
    {
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "FinWise.Tests", version = "1.0.0" }
            }
        };

        var json = JsonSerializer.Serialize(initRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        Logger.LogInformation("Initializing MCP session...");

        var initHttpRequest = new HttpRequestMessage(HttpMethod.Post, $"{McpBaseUrl}/mcp")
        {
            Content = content
        };
        using var response = await HttpClient.SendAsync(initHttpRequest, HttpCompletionOption.ResponseHeadersRead);

        Logger.LogInformation("MCP Initialize Response Status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"MCP initialize failed: {response.StatusCode}, Content: {responseContent}");
        }

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            SessionId = sessionIds.First();
            Logger.LogInformation("MCP Session established: {SessionId}", SessionId);
            Output.WriteLine($"Test session ID: {SessionId}");
            return SessionId;
        }

        throw new InvalidOperationException("MCP server did not return a session ID");
    }

    /// <summary>
    /// Creates a new MCP session. Returns the new session ID
    /// but does NOT update the default SessionId.
    /// </summary>
    protected async Task<string> InitializeNewMcpSession()
    {
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "FinWise.Tests", version = "1.0.0" }
            }
        };

        var json = JsonSerializer.Serialize(initRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        Logger.LogInformation("Initializing new MCP session...");

        var newInitHttpRequest = new HttpRequestMessage(HttpMethod.Post, $"{McpBaseUrl}/mcp")
        {
            Content = content
        };
        using var response = await HttpClient.SendAsync(newInitHttpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"MCP initialize failed: {response.StatusCode}, Content: {responseContent}");
        }

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            var newSessionId = sessionIds.First();
            Logger.LogInformation("New MCP Session established: {SessionId}", newSessionId);
            return newSessionId;
        }

        throw new InvalidOperationException("MCP server did not return a session ID");
    }

    protected async Task SetupTestProfile()
    {
        Output.WriteLine("=== SETTING UP TEST PROFILE ===");

        await CallFinancialAdviceTool("Please, give me personalized financial advice");
        await CallFinancialAdviceTool("delatorre@outlook.com");
        await CallFinancialAdviceTool("Moderate");
        await CallFinancialAdviceTool("Increase profit");
        await CallFinancialAdviceTool("Long-term");

        Output.WriteLine("Test profile setup completed");
    }

    protected async Task SetupTestProfileWithEmail(string email, string? sessionId = null)
    {
        Output.WriteLine($"=== SETTING UP TEST PROFILE ({email}) ===");

        await CallFinancialAdviceTool("Please, give me personalized financial advice", sessionId);
        await CallFinancialAdviceTool(email, sessionId);
        await CallFinancialAdviceTool("Moderate", sessionId);
        await CallFinancialAdviceTool("Increase profit", sessionId);
        await CallFinancialAdviceTool("Long-term", sessionId);

        Output.WriteLine("Test profile setup completed");
    }

    protected async Task<string> CallFinancialAdviceTool(string query, string? sessionId = null)
    {
        return await CallMcpToolAsync("run_finwise_workflow", new { query }, sessionId);
    }

    protected async Task<string> CallResetSessionTool(string? sessionId = null)
    {
        return await CallMcpToolAsync("reset_conversation", new { }, sessionId);
    }

    /// <summary>
    /// Sends a tools/call JSON-RPC request to the MCP server, handles SSE/plain-JSON
    /// responses, and returns the extracted text content.
    /// </summary>
    protected async Task<string> CallMcpToolAsync(string toolName, object arguments, string? sessionId = null)
    {
        var actualSessionId = sessionId ?? SessionId;

        var toolCallRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        var json = JsonSerializer.Serialize(toolCallRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("MCP-Session-Id", actualSessionId);

        Logger.LogInformation("Calling MCP tool {Tool} with args: {Args}, Session: {SessionId}",
            toolName, json, actualSessionId);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{McpBaseUrl}/mcp")
            {
                Content = content
            };
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.LogInformation("MCP Response Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"MCP request failed: {response.StatusCode}, Content: {errorContent}");
            }

            var responseContent = await ReadMcpResponseAsync(response);

            Logger.LogInformation("MCP Response Status: {StatusCode}, Content: {Content}",
                response.StatusCode, responseContent);

            return ParseMcpResponse(responseContent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error calling MCP tool {Tool}", toolName);
            throw new InvalidOperationException($"Failed to call MCP tool '{toolName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads an MCP HTTP response, handling both SSE (text/event-stream) and plain JSON formats.
    /// Uses streaming read for SSE to avoid blocking on open connections.
    /// </summary>
    private static async Task<string> ReadMcpResponseAsync(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType == "text/event-stream")
        {
            // SSE: read line-by-line, extract first complete data event
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? dataContent = null;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataContent = line.Substring(5).Trim();
                }
                else if (line == "" && dataContent != null)
                {
                    // Empty line = end of SSE event
                    return dataContent;
                }
            }

            // If we got data but stream ended without empty line separator
            if (dataContent != null)
                return dataContent;

            throw new InvalidOperationException("SSE response contained no data events");
        }
        else
        {
            // Plain JSON response — safe to read fully
            return await response.Content.ReadAsStringAsync();
        }
    }

    /// <summary>
    /// Parses an MCP response that may be SSE or plain JSON format.
    /// </summary>
    protected static string ParseMcpResponse(string responseContent)
    {
        string jsonContent;
        if (responseContent.StartsWith("event:"))
        {
            var lines = responseContent.Split('\n');
            var dataLine = lines.FirstOrDefault(l => l.StartsWith("data:"));
            if (dataLine != null)
            {
                jsonContent = dataLine.Substring(5).Trim();
            }
            else
            {
                throw new InvalidOperationException("SSE response missing 'data:' line");
            }
        }
        else
        {
            jsonContent = responseContent;
        }

        var jsonDoc = JsonDocument.Parse(jsonContent);

        if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
        {
            var errorMessage = errorElement.GetProperty("message").GetString();
            throw new InvalidOperationException($"MCP tool error: {errorMessage}");
        }

        if (jsonDoc.RootElement.TryGetProperty("result", out var resultElement))
        {
            if (resultElement.TryGetProperty("content", out var contentElement))
            {
                if (contentElement.ValueKind == JsonValueKind.Array)
                {
                    var firstContent = contentElement.EnumerateArray().FirstOrDefault();
                    if (firstContent.TryGetProperty("text", out var textElement))
                    {
                        return textElement.GetString() ?? "";
                    }
                }
                else if (contentElement.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? "";
                }
            }

            if (resultElement.ValueKind == JsonValueKind.String)
            {
                return resultElement.GetString() ?? "";
            }
        }

        return jsonContent;
    }

    protected static string TruncateForLog(string text, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "...";
    }

    public void Dispose()
    {
        HttpClient?.Dispose();
    }
}

/// <summary>
/// Custom logger provider for outputting logs to xUnit test output.
/// </summary>
public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore logging failures in tests
        }
    }
}
