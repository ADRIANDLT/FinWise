using System;
using System.Threading;
using Microsoft.Extensions.AI;

namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Holds the in-flight message history during one workflow execution. Not persisted —
/// exists only for the duration of ProcessMessageAsync. All agents in the workflow
/// can access it via AsyncLocal (see <see cref="AgentSessionRunContext"/>).
/// </summary>
/// <param name="AgentSessionId">
/// The active agent session identifier.
/// Called <c>conversationId</c> in the SDK's <c>AgentSessionStore</c> — same concept.
/// </param>
/// <param name="Messages">The current message history snapshot.</param>
public sealed record AgentSessionRunSnapshot(string AgentSessionId, IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// Provides ambient access to the current <see cref="AgentSessionRunSnapshot"/> during
/// a workflow run. Uses AsyncLocal so nested async operations (including agent tool
/// implementations) can access the same context safely.
/// </summary>
public static class AgentSessionRunContext
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

    private static readonly AsyncLocal<AgentSessionRunSnapshot?> CurrentSnapshot = new();

    /// <summary>
    /// Gets the active agent session snapshot for the current async flow, if any.
    /// </summary>
    public static AgentSessionRunSnapshot? Current => CurrentSnapshot.Value;

    /// <summary>
    /// Pushes an agent session snapshot onto the context stack. Returns an <see cref="IDisposable"/>
    /// scope that restores the previous snapshot when disposed.
    /// </summary>
    public static IDisposable Push(AgentSessionRunSnapshot snapshot)
    {
        var previous = CurrentSnapshot.Value;
        CurrentSnapshot.Value = snapshot;
        return new Scope(() => CurrentSnapshot.Value = previous);
    }
}
