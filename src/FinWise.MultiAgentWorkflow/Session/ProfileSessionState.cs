namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Structured, session-scoped state recording whether the user's profile is complete.
/// Stored in <c>AgentSession.StateBag</c> (serialized with the session) — replaces the
/// legacy <c>PROFILE_READY:</c> chat-history text marker.
/// </summary>
public sealed class ProfileSessionState
{
    /// <summary>True once the user's profile has been confirmed complete at least once.</summary>
    public bool ProfileReady { get; set; }

    /// <summary>The user's identifier (email) captured when the profile became complete, if known.</summary>
    public string? UserId { get; set; }
}
