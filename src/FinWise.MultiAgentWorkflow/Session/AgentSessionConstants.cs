using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Shared constants for session and conversation management.
/// </summary>
internal static class AgentSessionConstants
{
    /// <summary>
    /// Legacy marker formerly emitted by the profile agent when a user's profile is complete.
    /// <b>FALLBACK ONLY:</b> Profile readiness is now tracked via structured session state
    /// (<see cref="ProfileSessionState"/> in <c>AgentSession.StateBag</c>). This marker is
    /// retained solely to migrate legacy sessions created before structured state existed.
    /// </summary>
    internal const string ProfileReadyMarker = "PROFILE_READY:";

    /// <summary>
    /// <b>FALLBACK ONLY:</b> Checks whether the conversation history contains a legacy
    /// PROFILE_READY marker. Used solely to migrate legacy sessions that predate the
    /// structured <see cref="ProfileSessionState"/>; new readiness is determined from session state.
    /// </summary>
    internal static bool IsProfileReady(List<ChatMessage> history)
    {
        return history.Any(m =>
            m.Role == ChatRole.Assistant &&
            m.Text?.Contains(ProfileReadyMarker, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Pattern to extract email from the PROFILE_READY marker in conversation history.
    /// Matches "email=user@example.com" in "PROFILE_READY: email=user@example.com ...".
    /// </summary>
    private static readonly Regex ProfileReadyEmailPattern = new(
        @"email=([^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// <b>FALLBACK ONLY:</b> Extracts the userId (email) from the legacy PROFILE_READY marker
    /// in message history. The marker format is:
    /// "PROFILE_READY: email=user@example.com risk=... goals=... timeframe=...".
    /// Used solely to migrate legacy sessions that predate the structured
    /// <see cref="ProfileSessionState"/>; new sessions capture the userId in session state.
    /// </summary>
    /// <returns>The email address if found, null otherwise.</returns>
    internal static string? ExtractUserIdFromMessageHistory(List<ChatMessage> history)
    {
        var profileReadyMessage = history
            .Where(m => m.Role == ChatRole.Assistant && m.Text != null)
            .Select(m => m.Text)
            .FirstOrDefault(text => text!.Contains(ProfileReadyMarker, StringComparison.OrdinalIgnoreCase));

        if (profileReadyMessage != null)
        {
            var emailMatch = ProfileReadyEmailPattern.Match(profileReadyMessage);

            if (emailMatch.Success && emailMatch.Groups.Count > 1)
            {
                return emailMatch.Groups[1].Value;
            }
        }

        return null;
    }
}
