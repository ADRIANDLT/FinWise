using System.Text.RegularExpressions;

namespace AIResearchAgents.Core;

/// <summary>
/// Result of a research execution by an AI research agent.
/// </summary>
public sealed record ResearchResult
{
    public required bool IsSuccess { get; init; }
    public string? Summary { get; init; }
    public string? ErrorMessage { get; init; }
    public double ElapsedSeconds { get; init; }

    /// <summary>
    /// Ready-to-display formatted output including banner header and summary content.
    /// </summary>
    public string? FormattedOutput { get; init; }

    /// <summary>
    /// Strips markdown links from text: [Title](url) → Title.
    /// Also removes stray Bing citation brackets like 【N:N†source】.
    /// Adds spacing to prevent adjacent text from merging.
    /// </summary>
    public static string StripMarkdownLinks(string text)
    {
        // Replace [text](url) with space + text to prevent adjacent text merging
        var result = Regex.Replace(text, @"\[([^\]]+)\]\(https?://[^)]+\)", " $1");
        // Remove stray Bing citation bracket markers
        result = Regex.Replace(result, @"[\u3010\u3011]", "");
        // Clean up any resulting double spaces
        result = Regex.Replace(result, @"  +", " ");
        return result;
    }
}
