using Microsoft.Extensions.AI;

namespace FinWise.Orchestrator;

/// <summary>
/// User profile data transfer object for in-memory storage.
/// UserId stores the user's email address.
/// All profile fields are free-form strings - just context for the advisor agent.
/// Supports incremental/progressive saving - fields can be null until user provides them.
/// </summary>
public record UserProfileDto(
    string UserId,
    string? RiskTolerance,
    string? InvestmentGoals,
    string? InvestmentTimeframe
)
{
    /// <summary>
    /// Checks if the profile has all required fields filled in.
    /// A complete profile has: email (UserId), risk tolerance, investment goals, and timeframe.
    /// </summary>
    public bool IsComplete => 
        !string.IsNullOrWhiteSpace(UserId) &&
        !string.IsNullOrWhiteSpace(RiskTolerance) &&
        !string.IsNullOrWhiteSpace(InvestmentGoals) &&
        !string.IsNullOrWhiteSpace(InvestmentTimeframe);

    /// <summary>
    /// Creates a new profile with updated fields, preserving existing values for fields not provided.
    /// </summary>
    public UserProfileDto WithUpdates(string? risk = null, string? goals = null, string? timeframe = null)
    {
        return new UserProfileDto(
            UserId,
            !string.IsNullOrWhiteSpace(risk) ? risk : RiskTolerance,
            !string.IsNullOrWhiteSpace(goals) ? goals : InvestmentGoals,
            !string.IsNullOrWhiteSpace(timeframe) ? timeframe : InvestmentTimeframe
        );
    }
};

/// <summary>
/// Workflow execution context for multi-agent orchestration.
/// UserId currently stores email address.
/// </summary>
public record WorkflowExecutionContext(
    string UserId,
    string Query,
    DateTime RequestTime
);

/// <summary>
/// Conversation metadata including user relationship and message history.
/// The userId field acts as a foreign key to associate conversations with users.
/// </summary>
public record ConversationMetadata(
    string ConversationId,
    string UserId,  // Foreign key - currently stores email address
    DateTime CreatedAt,
    DateTime LastMessageAt,
    List<ChatMessage> Messages
)
{
    // Mutable Messages property to allow updates
    public List<ChatMessage> Messages { get; set; } = Messages;
}
