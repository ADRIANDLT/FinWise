using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores;
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
/// <see cref="AgentSessionStore"/>, and reset when the user explicitly requests it
/// or starts a new MCP session.
///
/// The session is created from the orchestrator agent but stores messages from all
/// agents in the handoff workflow.
///
/// Per Microsoft Agent Framework documentation:
/// - AgentSession is the abstraction for conversation state
/// - AIAgent instances are stateless; all state is preserved in AgentSession
/// - The SDK's AgentSessionStore handles serialization/deserialization internally
/// - Messages are accessed via TryGetInMemoryChatHistory() / SetInMemoryChatHistory()
///   extension methods (StateBag-based, added in RC4)
///
/// <b>Naming note:</b> The SDK's <c>AgentSessionStore</c> uses <c>conversationId</c>
/// as its storage key parameter. We use <c>agentSessionId</c> — same concept, different
/// name to avoid confusion with MCP session IDs in this codebase.
///
/// This manager handles:
/// - Session creation and retrieval (delegates to SDK's AgentSessionStore)
/// - Message access via SDK extension methods
/// - Session clearing (no-op for in-memory; orphaned keys are harmless)
/// </summary>
public class AgentSessionManager
{
    private readonly AgentSessionStore _sessionStore;

    public AgentSessionManager(AgentSessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    /// <summary>
    /// Gets or creates an AgentSession for the given agent session.
    /// The SDK's AgentSessionStore handles deserialization or creation internally.
    /// Messages are extracted from the session via TryGetInMemoryChatHistory().
    /// </summary>
    /// <param name="agent">The agent used for session operations.</param>
    /// <param name="agentSessionId">The agent session identifier.</param>
    /// <returns>An AgentSession (new or restored) and its message history.</returns>
    public async Task<(AgentSession Session, List<ChatMessage> Messages)> GetOrCreateSessionAsync(AIAgent agent, string agentSessionId)
    {
        AgentSession session = await _sessionStore.GetSessionAsync(agent, agentSessionId);

        // SDK preview: TryGetInMemoryChatHistory out param may be null even when returning true
        if (session.TryGetInMemoryChatHistory(out List<ChatMessage>? messages) && messages is not null)
        {
            Log.Debug("Restored AgentSession for {AgentSessionId} with {MessageCount} messages",
                agentSessionId, messages.Count);
            return (session, messages);
        }

        Log.Debug("Creating new AgentSession for {AgentSessionId}", agentSessionId);
        return (session, []);
    }

    /// <summary>
    /// Persists an AgentSession. Messages are written into the session via
    /// SetInMemoryChatHistory() before the SDK's store serializes the whole session.
    /// </summary>
    /// <param name="agentSessionId">The agent session identifier.</param>
    /// <param name="session">The AgentSession to persist.</param>
    /// <param name="agent">The AIAgent used for session operations.</param>
    /// <param name="messages">The conversation messages to persist with the session.</param>
    public async Task PersistSessionAsync(string agentSessionId, AgentSession session, AIAgent agent, List<ChatMessage> messages)
    {
        session.SetInMemoryChatHistory(messages);
        await _sessionStore.SaveSessionAsync(agent, agentSessionId, session);

        Log.Debug("Persisted AgentSession for {AgentSessionId} (messages: {MessageCount})",
            agentSessionId, messages.Count);
    }

    /// <summary>
    /// Clears an agent session. For InMemoryAgentSessionStore this is a no-op — orphaned keys
    /// are harmless. For RedisAgentSessionStore, performs an explicit key delete with TTL as safety net.
    /// </summary>
    public async Task ClearSessionAsync(string agentSessionId)
    {
        if (_sessionStore is IClearableSessionStore clearable)
        {
            await clearable.ClearSessionAsync(agentSessionId);
            Log.Debug("Cleared session for {AgentSessionId}", agentSessionId);
        }
        else
        {
            Log.Debug("ClearSessionAsync called for {AgentSessionId} (no-op for in-memory store)", agentSessionId);
        }
    }
}

