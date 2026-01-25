using System;
using System.Threading;
using Microsoft.Extensions.AI;

namespace FinWise.Orchestrator;

/// <summary>
/// Maintains the conversation snapshot for the current workflow execution so
/// tool implementations can validate inputs against recently provided user messages.
/// </summary>
public sealed record ConversationRunSnapshot(string ConversationId, IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// Provides ambient access to the current conversation snapshot during a workflow run.
/// Uses AsyncLocal so nested async operations can access the same context safely.
/// </summary>
public static class ConversationRunContext
{
    private sealed class Scope(Action onDispose) : IDisposable
    {
        private readonly Action _onDispose = onDispose;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }

    private static readonly AsyncLocal<ConversationRunSnapshot?> CurrentSnapshot = new();

    /// <summary>
    /// Gets the active conversation snapshot for the current async flow, if any.
    /// </summary>
    public static ConversationRunSnapshot? Current => CurrentSnapshot.Value;

    /// <summary>
    /// Pushes a conversation snapshot onto the context stack. Returns an <see cref="IDisposable"/>
    /// scope that restores the previous snapshot when disposed.
    /// </summary>
    public static IDisposable Push(ConversationRunSnapshot snapshot)
    {
        var previous = CurrentSnapshot.Value;
        CurrentSnapshot.Value = snapshot;
        return new Scope(() => CurrentSnapshot.Value = previous);
    }
}
