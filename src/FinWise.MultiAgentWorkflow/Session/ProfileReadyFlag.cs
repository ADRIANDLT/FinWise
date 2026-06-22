namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Mutable token set by the profile agent's tools when the user's profile is confirmed
/// complete during a workflow run. The parent (ProcessMessageAsync) initializes it before
/// awaiting the workflow; tools mutate the shared reference (visible via AsyncLocal).
/// </summary>
public sealed class ProfileReadyToken
{
    /// <summary>True once a tool has marked the profile complete during this run.</summary>
    public bool IsReady { get; private set; }

    /// <summary>The user's identifier (email) captured when marked ready, if provided.</summary>
    public string? UserId { get; private set; }

    /// <summary>Marks the profile as complete, optionally recording the user identifier.</summary>
    public void MarkReady(string? userId = null)
    {
        IsReady = true;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            UserId = userId;
        }
    }
}

/// <summary>
/// Ambient access to the current <see cref="ProfileReadyToken"/> during a workflow run.
/// </summary>
public static class ProfileReadyFlag
{
    private static readonly AsyncLocal<ProfileReadyToken?> _current = new();

    /// <summary>Gets the current token, if initialized by the parent scope.</summary>
    public static ProfileReadyToken? Current => _current.Value;

    /// <summary>
    /// Called by ProcessMessageAsync BEFORE awaiting workflow execution. Creates a token
    /// seeded with the session's existing profile-ready state so it stays ready on later turns.
    /// </summary>
    public static ProfileReadyToken Initialize(bool alreadyReady = false, string? userId = null)
    {
        var token = new ProfileReadyToken();
        if (alreadyReady)
        {
            token.MarkReady(userId);
        }
        _current.Value = token;
        return token;
    }

    /// <summary>Clears the AsyncLocal reference after use.</summary>
    public static void Clear() => _current.Value = null;
}
