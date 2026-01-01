using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace FinWise.Orchestrator;

/// <summary>
/// In-memory implementation of conversation storage for v0.1.
/// Thread-safe using ConcurrentDictionary.
/// Will be replaced with database-backed implementation in v0.2+.
/// </summary>
public class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public Task<List<ChatMessage>> GetConversationHistoryAsync(string userId)
    {
        if (_conversations.TryGetValue(userId, out var history))
        {
            // Return a copy to prevent external modifications
            return Task.FromResult(new List<ChatMessage>(history));
        }

        return Task.FromResult(new List<ChatMessage>());
    }

    public Task AppendMessageAsync(string userId, ChatMessage message)
    {
        _conversations.AddOrUpdate(
            userId,
            _ => new List<ChatMessage> { message },
            (_, existing) =>
            {
                existing.Add(message);
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task SetConversationHistoryAsync(string userId, List<ChatMessage> messages)
    {
        _conversations[userId] = new List<ChatMessage>(messages);
        return Task.CompletedTask;
    }

    public Task ClearConversationAsync(string userId)
    {
        _conversations.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
