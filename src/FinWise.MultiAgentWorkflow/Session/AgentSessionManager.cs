using System.Text.Json;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Manages AgentSession lifecycle using Microsoft Agent Framework patterns.
///
/// <b>AgentSession definition:</b>
/// An AgentSession is a stateful container that holds the complete message history
/// and context for one interaction thread between a user and the multi-agent workflow.
/// Each <c>agentSessionId</c> maps to exactly one AgentSession. All agents in the
/// workflow (orchestrator, profile, advisor) share this session — it's scoped to
/// the user's interaction, not to an individual agent. Identified by
/// <c>agentSessionId</c> (GUID string), persisted between requests via
/// <see cref="IAgentSessionStore"/>, and reset when the user explicitly requests it
/// or starts a new MCP session.
///
/// The session is created from the orchestrator agent but stores messages from all
/// agents in the handoff workflow.
///
/// Per Microsoft Agent Framework documentation:
/// - AgentSession is the abstraction for conversation state
/// - AIAgent instances are stateless; all state is preserved in AgentSession
/// - Use agent.SerializeSessionAsync(session) to persist and agent.DeserializeSessionAsync() to resume
///
/// <b>Naming note:</b> The SDK's <c>AgentSessionStore</c> uses <c>conversationId</c>
/// as its storage key parameter. We use <c>agentSessionId</c> — same concept, different
/// name to avoid confusion with MCP session IDs in this codebase.
///
/// This manager handles:
/// - Session creation and retrieval
/// - Session serialization/deserialization for persistence
/// - userId resolution for user profile association
/// </summary>
public class AgentSessionManager
{
    private readonly IAgentSessionStore _sessionStore;

    public AgentSessionManager(IAgentSessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    /// <summary>
    /// Gets or creates an AgentSession for the given agent session.
    /// Uses agent.DeserializeSessionAsync() to restore serialized session state.
    /// </summary>
    /// <param name="agent">The agent to use for session deserialization.</param>
    /// <param name="agentSessionId">The agent session identifier.</param>
    /// <returns>An AgentSession (new or restored from storage).</returns>
    public async Task<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string agentSessionId)
    {
        var sessionData = await _sessionStore.GetSessionDataAsync(agentSessionId);

        if (sessionData == null)
        {
            // No existing session - create new one
            Log.Debug("Creating new AgentSession for {AgentSessionId}", agentSessionId);
            return await agent.CreateSessionAsync();
        }

        try
        {
            // Deserialize existing session using Microsoft Agent Framework pattern
            AgentSession resumedSession = await agent.DeserializeSessionAsync(sessionData.SerializedSession);

            // Debug: Check if message store is properly restored
            var restoredStore = resumedSession.GetService<InMemoryChatHistoryProvider>();
            var restoredCount = restoredStore?.GetMessages(resumedSession).Count ?? 0;
            Log.Debug("Restored AgentSession for {AgentSessionId} with {MessageCount} messages (expected), actual store has {ActualCount} messages, StoreType: {StoreType}",
                agentSessionId, sessionData.MessageCount, restoredCount, restoredStore?.GetType().Name ?? "null");
            return resumedSession;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deserialize session for {AgentSessionId}, creating new session",
                agentSessionId);
            return await agent.CreateSessionAsync();
        }
    }

    /// <summary>
    /// Persists an AgentSession using agent.SerializeSessionAsync(session) pattern.
    /// </summary>
    /// <param name="agentSessionId">The agent session identifier.</param>
    /// <param name="session">The AgentSession to persist.</param>
    /// <param name="agent">The AIAgent used to serialize the session.</param>
    /// <param name="userId">The user identifier (email) to associate.</param>
    /// <param name="messageCount">Number of messages in the session.</param>
    public async Task PersistSessionAsync(string agentSessionId, AgentSession session, AIAgent agent, string userId, int messageCount)
    {
        // Serialize session using Microsoft Agent Framework pattern
        JsonElement serializedSession = await agent.SerializeSessionAsync(session);

        var sessionData = new AgentSessionData
        {
            AgentSessionId = agentSessionId,
            UserId = userId,
            SerializedSession = serializedSession,
            MessageCount = messageCount,
            LastMessageAt = DateTime.UtcNow
        };

        await _sessionStore.SetSessionDataAsync(agentSessionId, sessionData);

        Log.Debug("Persisted AgentSession for {AgentSessionId} (userId: {UserId}, messages: {MessageCount})",
            agentSessionId, userId, messageCount);
    }

    /// <summary>
    /// Clears an agent session.
    /// </summary>
    public Task ClearSessionAsync(string agentSessionId)
    {
        return _sessionStore.ClearSessionAsync(agentSessionId);
    }

}

