using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Shared constants for session and conversation management.
/// </summary>
internal static class AgentSessionConstants
{
    /// <summary>
    /// Marker emitted by the profile agent when a user's profile is complete.
    /// Used by the orchestrator for routing and by session management for reset detection.
    /// </summary>
    internal const string ProfileReadyMarker = "PROFILE_READY:";

    /// <summary>
    /// Pattern to extract email from the PROFILE_READY marker in conversation history.
    /// Matches "email=user@example.com" in "PROFILE_READY: email=user@example.com ...".
    /// </summary>
    private static readonly Regex ProfileReadyEmailPattern = new(
        @"email=([^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the userId (email) from the PROFILE_READY marker in message history.
    /// The marker format is: "PROFILE_READY: email=user@example.com risk=... goals=... timeframe=..."
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
