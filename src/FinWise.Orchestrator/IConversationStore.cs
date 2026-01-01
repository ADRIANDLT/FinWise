using Microsoft.Extensions.AI;

namespace FinWise.Orchestrator;

/// <summary>
/// Contract for storing and retrieving conversation history.
/// Implementations can be in-memory (v0.1) or database-backed (v0.2+).
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Retrieves the conversation history for a specific user.
    /// </summary>
    /// <param name="userId">Unique identifier for the user.</param>
    /// <returns>List of chat messages in chronological order. Empty list if no history exists.</returns>
    Task<List<ChatMessage>> GetConversationHistoryAsync(string userId);

    /// <summary>
    /// Appends a message to the user's conversation history.
    /// </summary>
    /// <param name="userId">Unique identifier for the user.</param>
    /// <param name="message">The message to append.</param>
    Task AppendMessageAsync(string userId, ChatMessage message);

    /// <summary>
    /// Replaces the entire conversation history for a user.
    /// Used when the workflow generates multiple new messages.
    /// </summary>
    /// <param name="userId">Unique identifier for the user.</param>
    /// <param name="messages">Complete conversation history.</param>
    Task SetConversationHistoryAsync(string userId, List<ChatMessage> messages);

    /// <summary>
    /// Clears the conversation history for a specific user.
    /// </summary>
    /// <param name="userId">Unique identifier for the user.</param>
    Task ClearConversationAsync(string userId);
}
