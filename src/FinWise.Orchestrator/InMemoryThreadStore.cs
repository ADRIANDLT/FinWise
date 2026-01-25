using System.Collections.Concurrent;

namespace FinWise.Orchestrator;

/// <summary>
/// In-memory implementation of IThreadStore for development/testing.
/// Uses thread-safe ConcurrentDictionary for storage.
/// 
/// Per Microsoft Agent Framework patterns:
/// - Stores serialized AgentThread data (JsonElement from thread.Serialize())
/// - Supports resumption via agent.DeserializeThread()
/// 
/// Production: Replace with Cosmos DB implementation (v0.3+).
/// </summary>
public class InMemoryThreadStore : IThreadStore
{
    private readonly ConcurrentDictionary<string, ThreadData> _threads = new();

    /// <inheritdoc />
    public Task<ThreadData?> GetThreadDataAsync(string conversationId)
    {
        _threads.TryGetValue(conversationId, out var data);
        return Task.FromResult(data);
    }

    /// <inheritdoc />
    public Task<List<ThreadData>> GetThreadsByUserIdAsync(string userId)
    {
        var userThreads = _threads.Values
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastMessageAt)
            .ToList();
        return Task.FromResult(userThreads);
    }

    /// <inheritdoc />
    public Task SetThreadDataAsync(string conversationId, ThreadData data)
    {
        _threads[conversationId] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearThreadAsync(string conversationId)
    {
        _threads.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }
}
