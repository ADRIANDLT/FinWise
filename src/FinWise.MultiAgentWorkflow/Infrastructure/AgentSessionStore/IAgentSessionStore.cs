using System.Text.Json;

namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore;

/// <summary>
/// Contract for storing and retrieving AgentSession data.
/// Uses Microsoft Agent Framework session serialization pattern.
///
/// <b>SDK relationship:</b> The Microsoft Agent Framework ships its own
/// <c>AgentSessionStore</c> abstract class (in <c>Microsoft.Agents.AI.Hosting</c>)
/// with <c>SaveSessionAsync(agent, conversationId, session)</c> and
/// <c>GetSessionAsync(agent, conversationId)</c>. Our interface differs:
/// - We store pre-serialized <see cref="AgentSessionData"/> with extra metadata
///   (UserId, MessageCount, LastMessageAt) that the SDK contract doesn't support.
/// - We separate serialization (in <see cref="Session.AgentSessionManager"/>) from storage.
/// - We include <see cref="ClearSessionAsync"/> for explicit reset flows (SDK has no equivalent).
/// - We avoid depending on <c>Microsoft.Agents.AI.Hosting</c> to keep the class library lightweight.
///
/// <b>Note:</b> The SDK calls the storage key <c>conversationId</c>; we call it
/// <c>agentSessionId</c>. Same concept — a unique identifier for one interaction thread.
/// Our name was chosen to avoid confusion with MCP session IDs and to emphasize that
/// it identifies a FinWise-managed session lifecycle (create → use → reset → new ID).
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// Gets serialized session data by agent session ID.
    /// </summary>
    /// <param name="agentSessionId">
    /// Unique identifier for the agent session.
    /// Equivalent to <c>conversationId</c> in the SDK's <c>AgentSessionStore</c>.
    /// </param>
    Task<AgentSessionData?> GetSessionDataAsync(string agentSessionId);

    /// <summary>
    /// Saves serialized session data.
    /// </summary>
    Task SetSessionDataAsync(string agentSessionId, AgentSessionData data);

    /// <summary>
    /// Clears a session.
    /// </summary>
    Task ClearSessionAsync(string agentSessionId);
}

/// <summary>
/// Represents serialized AgentSession data for persistence.
/// Per Microsoft Agent Framework: agent.SerializeSessionAsync(session) returns JsonElement.
/// </summary>
public record AgentSessionData
{
    /// <summary>
    /// Unique agent session identifier.
    /// Called <c>conversationId</c> in the SDK's <c>AgentSessionStore</c>.
    /// </summary>
    public required string AgentSessionId { get; init; }

    /// <summary>
    /// User identifier (email address).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Serialized AgentSession state (from agent.SerializeSessionAsync(session)).
    /// Per Microsoft Agent Framework: "JsonElement serialized = await agent.SerializeSessionAsync(session);"
    /// </summary>
    public required JsonElement SerializedSession { get; init; }

    /// <summary>
    /// Number of messages in the session (for quick checks without deserialization).
    /// </summary>
    public int MessageCount { get; init; }

    /// <summary>
    /// Timestamp of last message (for session timeout detection).
    /// </summary>
    public DateTime LastMessageAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
