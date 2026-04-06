using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace FinWise.McpServer.Infrastructure.McpSession;

/// <summary>
/// Extracts the MCP Session ID from HTTP request headers.
/// Under 008.A, this ID is used directly as the agent session identifier — no mapping layer.
/// </summary>
public static class McpSessionAccessor
{
    public static string GetSessionId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("MCP-Session-Id", out var sessionHeader) &&
            !StringValues.IsNullOrEmpty(sessionHeader))
        {
            return sessionHeader.ToString();
        }

        throw new InvalidOperationException("Missing MCP-Session-Id header on HTTP request.");
    }
}
