using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.Orchestrator;

/// <summary>
/// Manages AgentThread lifecycle using Microsoft Agent Framework patterns.
/// 
/// Per Microsoft Agent Framework documentation:
/// - AgentThread is the abstraction for conversation state
/// - AIAgent instances are stateless; all state is preserved in AgentThread
/// - Use thread.Serialize() to persist and agent.DeserializeThread() to resume
/// 
/// This manager handles:
/// - Thread creation and retrieval
/// - Thread serialization/deserialization for persistence
/// - Session detection (new logical sessions when MCP clients restart)
/// - userId resolution for user profile association
/// </summary>
public class AgentThreadManager
{
    private readonly IThreadStore _threadStore;

    /// <summary>
    /// Session timeout after profile is complete. Once PROFILE_READY marker exists,
    /// if ANY time has passed (even a few seconds), we treat it as a new session.
    /// This is because MCP STDIO has no session identifiers - the server cannot tell
    /// when a new GitHub Copilot/Claude Desktop chat starts. 
    /// 
    /// Set to 0 to ALWAYS require email on new requests after profile is complete.
    /// This is the safest default since we cannot distinguish between:
    /// - Same user continuing in same chat (wants continuity)
    /// - Same user opening new chat tab (wants fresh start)
    /// - Different user entirely (security concern!)
    /// </summary>
    public TimeSpan SessionTimeoutAfterProfileComplete { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Session timeout during profile collection. While the profile flow is in progress
    /// (user is answering questions about email, risk, goals, timeframe), we allow
    /// a longer timeout so they can take time to respond.
    /// </summary>
    public TimeSpan SessionTimeoutDuringProfileCollection { get; set; } = TimeSpan.FromMinutes(10);

    public AgentThreadManager(IThreadStore threadStore)
    {
        _threadStore = threadStore;
    }

    /// <summary>
    /// Gets or creates an AgentThread for the given conversation.
    /// Uses agent.DeserializeThread() to restore serialized thread state.
    /// </summary>
    /// <param name="agent">The agent to use for thread deserialization.</param>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <returns>An AgentThread (new or restored from storage).</returns>
    public async Task<AgentThread> GetOrCreateThreadAsync(AIAgent agent, string conversationId)
    {
        var threadData = await _threadStore.GetThreadDataAsync(conversationId);
        
        if (threadData == null)
        {
            // No existing thread - create new one
            Log.Debug("Creating new AgentThread for conversation {ConversationId}", conversationId);
            return agent.GetNewThread();
        }

        try
        {
            // Deserialize existing thread using Microsoft Agent Framework pattern
            AgentThread resumedThread = agent.DeserializeThread(threadData.SerializedThread);
            
            // Debug: Check if message store is properly restored
            var restoredStore = resumedThread.GetService<InMemoryChatMessageStore>();
            var restoredCount = restoredStore?.Count ?? 0;
            Log.Debug("Restored AgentThread for conversation {ConversationId} with {MessageCount} messages (expected), actual store has {ActualCount} messages, StoreType: {StoreType}", 
                conversationId, threadData.MessageCount, restoredCount, restoredStore?.GetType().Name ?? "null");
            return resumedThread;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to deserialize thread for conversation {ConversationId}, creating new thread", 
                conversationId);
            return agent.GetNewThread();
        }
    }

    /// <summary>
    /// Persists an AgentThread using thread.Serialize() pattern.
    /// </summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="thread">The AgentThread to persist.</param>
    /// <param name="userId">The user identifier (email) to associate.</param>
    /// <param name="messageCount">Number of messages in the thread.</param>
    public async Task PersistThreadAsync(string conversationId, AgentThread thread, string userId, int messageCount)
    {
        // Serialize thread using Microsoft Agent Framework pattern
        JsonElement serializedThread = thread.Serialize();
        
        var threadData = new ThreadData
        {
            ConversationId = conversationId,
            UserId = userId,
            SerializedThread = serializedThread,
            MessageCount = messageCount,
            LastMessageAt = DateTime.UtcNow
        };

        await _threadStore.SetThreadDataAsync(conversationId, threadData);
        
        Log.Debug("Persisted AgentThread for conversation {ConversationId} (userId: {UserId}, messages: {MessageCount})", 
            conversationId, userId, messageCount);
    }

    /// <summary>
    /// Gets thread metadata without full deserialization.
    /// Used for session detection and userId resolution.
    /// </summary>
    public Task<ThreadData?> GetThreadMetadataAsync(string conversationId)
    {
        return _threadStore.GetThreadDataAsync(conversationId);
    }

    /// <summary>
    /// Detects if a new logical MCP client session has started.
    /// 
    /// Per Microsoft Agent Framework patterns for multi-turn conversations:
    /// - AgentThread maintains conversation state
    /// - Session detection uses message history and timestamps
    /// - Session timeout indicates user started fresh in a new MCP client tab
    /// 
    /// Problem: MCP STDIO transport doesn't provide session IDs, so when a user opens
    /// a new GitHub Copilot tab or Claude Desktop chat, the MCP server process continues
    /// with the same `currentConversationId` pointing to stale conversation history.
    /// 
    /// Solution: Once profile collection is complete (PROFILE_READY exists), we ALWAYS
    /// treat subsequent requests as new sessions requiring re-identification via email.
    /// This is the only safe approach since we cannot distinguish between:
    /// - Same user in same chat (would like continuity)
    /// - Same user in new chat tab (expects fresh start) 
    /// - Different user entirely (security issue!)
    /// </summary>
    /// <param name="conversationId">The current conversation ID (may be stale).</param>
    /// <param name="messages">The current messages to check for PROFILE_READY marker.</param>
    /// <returns>True if this appears to be a new logical session that needs reset.</returns>
    public async Task<bool> IsNewLogicalSessionAsync(string? conversationId, IEnumerable<ChatMessage> messages)
    {
        // No existing conversation → definitely new
        if (string.IsNullOrEmpty(conversationId))
            return true;

        var threadData = await _threadStore.GetThreadDataAsync(conversationId);
        
        // Thread doesn't exist or is empty → new session
        if (threadData == null || threadData.MessageCount == 0)
            return true;

        // Check if existing conversation has PROFILE_READY marker
        // PROFILE_READY only appears AFTER successful email collection + profile load/create
        bool hasProfileReady = messages
            .Any(m => m.Role == ChatRole.Assistant && 
                      m.Text?.Contains("PROFILE_READY:", StringComparison.OrdinalIgnoreCase) == true);

        var timeSinceLastMessage = DateTime.UtcNow - threadData.LastMessageAt;

        if (!hasProfileReady)
        {
            // Profile flow is still in progress (collecting email, risk, goals, timeframe)
            // Use the longer timeout for profile collection
            bool profileFlowTimedOut = timeSinceLastMessage > SessionTimeoutDuringProfileCollection;
            
            if (profileFlowTimedOut)
            {
                Log.Information(
                    "Profile collection timed out: Conversation {ConversationId} was mid-profile-flow " +
                    "but {TimeSinceLastMessage} elapsed (threshold: {Threshold}). Starting fresh.",
                    conversationId,
                    timeSinceLastMessage.ToString(@"hh\:mm\:ss"),
                    SessionTimeoutDuringProfileCollection);
                return true;
            }
            
            // Still within timeout, continue profile collection
            Log.Debug("Continuing existing conversation {ConversationId} - profile flow in progress ({MessageCount} messages)", 
                conversationId, threadData.MessageCount);
            return false;
        }

        // PROFILE_READY exists - profile collection is complete
        // Now we must be very careful: we can't tell if this is the same user or a new user!
        // Use the stricter timeout (default: 0 = always new session)
        bool sessionTimedOut = timeSinceLastMessage > SessionTimeoutAfterProfileComplete;

        if (sessionTimedOut)
        {
            Log.Information(
                "New session after profile complete: Conversation {ConversationId} has PROFILE_READY marker " +
                "and {TimeSinceLastMessage} elapsed (threshold: {Threshold}). " +
                "Requiring re-identification via email. (Had {MessageCount} messages)", 
                conversationId,
                timeSinceLastMessage.ToString(@"hh\:mm\:ss"),
                SessionTimeoutAfterProfileComplete,
                threadData.MessageCount);
            return true;
        }

        // Profile is ready and session hasn't timed out → continue existing conversation
        Log.Debug("Continuing existing conversation {ConversationId} - profile ready, session active ({TimeSinceLastMessage} since last message)", 
            conversationId, timeSinceLastMessage.ToString(@"mm\:ss"));
        return false;
    }

    /// <summary>
    /// Clears a conversation thread.
    /// </summary>
    public Task ClearThreadAsync(string conversationId)
    {
        return _threadStore.ClearThreadAsync(conversationId);
    }

    /// <summary>
    /// Gets all thread data for a user.
    /// </summary>
    public Task<List<ThreadData>> GetThreadsByUserIdAsync(string userId)
    {
        return _threadStore.GetThreadsByUserIdAsync(userId);
    }
}

/// <summary>
/// Contract for storing and retrieving AgentThread data.
/// Uses Microsoft Agent Framework thread serialization pattern.
/// </summary>
public interface IThreadStore
{
    /// <summary>
    /// Gets serialized thread data by conversation ID.
    /// </summary>
    Task<ThreadData?> GetThreadDataAsync(string conversationId);

    /// <summary>
    /// Gets all threads for a user.
    /// </summary>
    Task<List<ThreadData>> GetThreadsByUserIdAsync(string userId);

    /// <summary>
    /// Saves serialized thread data.
    /// </summary>
    Task SetThreadDataAsync(string conversationId, ThreadData data);

    /// <summary>
    /// Clears a thread.
    /// </summary>
    Task ClearThreadAsync(string conversationId);
}

/// <summary>
/// Represents serialized AgentThread data for persistence.
/// Per Microsoft Agent Framework: thread.Serialize() returns JsonElement.
/// </summary>
public record ThreadData
{
    /// <summary>
    /// Unique conversation identifier.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// User identifier (email address).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Serialized AgentThread state (from thread.Serialize()).
    /// Per Microsoft Agent Framework: "JsonElement serialized = await thread.SerializeAsync();"
    /// </summary>
    public required JsonElement SerializedThread { get; init; }

    /// <summary>
    /// Number of messages in the thread (for quick checks without deserialization).
    /// </summary>
    public int MessageCount { get; init; }

    /// <summary>
    /// Timestamp of last message (for session timeout detection).
    /// </summary>
    public DateTime LastMessageAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the thread was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
