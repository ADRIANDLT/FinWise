using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;

namespace FinWise.MultiAgentWorkflow.Session;

/// <summary>
/// Evaluates whether a session should be reset based on explicit user requests.
/// 
/// Session management philosophy:
/// - Within the same VS Code instance, treat as same user (same MCP session ID)
/// - User can explicitly request re-identification with phrases like "re-identify", "my email is...", etc.
/// - New VS Code instance = new MCP session ID = fresh start (asks for email)
/// </summary>
internal static class AgentSessionResetEvaluator
{
    /// <summary>
    /// Explicit phrases that indicate user wants to reset their session.
    /// These triggers reset the session requiring re-identification via email.
    /// </summary>
    private static readonly string[] ResetTriggers =
    [
        // Session management
        "start new session",
        "start new conversation",
        "new conversation",
        "new session",
        "start over",
        "reset conversation",
        "reset session",
        "let's start over",
        "logoff",
        "log off",
        "logout",
        "log out",
        "sign out",
        "end session",
        // Re-identification triggers
        "re-identify",
        "reidentify", 
        "identify me",
        "switch user",
        "change user",
        "i am someone else",
        "different user",
        "not me",
        "wrong user",
        "use different email",
        "change email",
        "my email is",
        "this is my email"
    ];

    /// <summary>
    /// Determines if the session should be reset based on explicit user request.
    /// Only triggers on explicit reset/re-identification phrases when a profile is already established.
    /// </summary>
    public static bool ShouldResetSession(IReadOnlyList<ChatMessage> messageHistory, string? query)
    {
        if (messageHistory.Count == 0)
        {
            return false;
        }

        var normalizedQuery = Normalize(query);
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return false;
        }

        // Only consider reset if profile is already established
        if (!HasProfileReady(messageHistory))
        {
            return false;
        }

        // Reset on explicit re-identification requests
        return MatchesResetTrigger(normalizedQuery);
    }

    private static bool HasProfileReady(IReadOnlyList<ChatMessage> history)
    {
        return history.Any(m => m.Role == ChatRole.Assistant &&
            !string.IsNullOrWhiteSpace(m.Text) &&
            m.Text.Contains(AgentSessionConstants.ProfileReadyMarker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesResetTrigger(string normalizedQuery)
    {
        return ResetTriggers.Any(trigger => normalizedQuery.Contains(trigger, StringComparison.Ordinal));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }
}
