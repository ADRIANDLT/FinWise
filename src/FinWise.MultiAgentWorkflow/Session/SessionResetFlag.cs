namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Mutable token set by the orchestrator's request_session_reset tool during workflow execution.
/// The parent (ProcessMessageAsync) creates and pushes the token before awaiting the workflow.
/// The child (tool execution) mutates the shared object. Because AsyncLocal copies references
/// (not objects), the mutation is visible to the parent after the await completes.
/// </summary>
public sealed class SessionResetToken
{
    public bool IsRequested { get; private set; }
    public void Request() => IsRequested = true;
}

/// <summary>
/// Ambient access to the current <see cref="SessionResetToken"/> during a workflow run.
/// Parent initializes via <see cref="Initialize"/>; tools read via <see cref="Current"/>.
/// </summary>
public static class SessionResetFlag
{
    private static readonly AsyncLocal<SessionResetToken?> _current = new();

    /// <summary>
    /// Gets the current token, if initialized by the parent scope.
    /// </summary>
    public static SessionResetToken? Current => _current.Value;

    /// <summary>
    /// Called by ProcessMessageAsync BEFORE awaiting workflow execution.
    /// Creates and stores a fresh token that tools can mutate.
    /// </summary>
    public static SessionResetToken Initialize()
    {
        var token = new SessionResetToken();
        _current.Value = token;
        return token;
    }

    /// <summary>
    /// Clears the AsyncLocal reference after use.
    /// </summary>
    public static void Clear() => _current.Value = null;
}
