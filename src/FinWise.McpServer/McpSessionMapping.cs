namespace FinWise.McpServer;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Maps MCP session IDs (from HTTP headers) to agent session IDs.
/// MCP transport concern — only used by the MCP host.
///
/// Relationship: 1 MCP Session → 1 AgentSession (at any time).
/// When a reset occurs, the mapping is updated to point to a new agentSessionId.
/// Two MCP sessions never share the same AgentSession.
/// User profiles (in IUserProfileStore) ARE shared across sessions via email lookup.
/// </summary>
public class McpSessionMapping
{
    private readonly ConcurrentDictionary<string, string> _mcpToAgentSessionMap = new(StringComparer.Ordinal);

    /// <summary>
    /// Extracts MCP-Session-Id from HTTP request headers.
    /// </summary>
    public static string GetSessionId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("MCP-Session-Id", out var sessionHeader) &&
            !StringValues.IsNullOrEmpty(sessionHeader))
        {
            return sessionHeader.ToString();
        }

        throw new InvalidOperationException("Missing MCP-Session-Id header on HTTP request.");
    }

    public string GetOrCreateAgentSessionId(string sessionId)
    {
        return _mcpToAgentSessionMap.GetOrAdd(sessionId, static _ => Guid.NewGuid().ToString());
    }

    public string? TryGetAgentSessionId(string sessionId)
    {
        return _mcpToAgentSessionMap.TryGetValue(sessionId, out var id) ? id : null;
    }

    public void UpdateAgentSessionId(string sessionId, string newAgentSessionId)
    {
        _mcpToAgentSessionMap[sessionId] = newAgentSessionId;
    }
}
