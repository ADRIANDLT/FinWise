namespace FinWise.MultiAgentWorkflow.DomainModel;

/// <summary>
/// User profile domain model for storage.
/// UserId stores the user's email address.
/// All profile fields are free-form strings - just context for the advisor agent.
/// Supports incremental/progressive saving - fields can be null until user provides them.
/// Defined as a record (not class) for immutability: updates create new instances via WithUpdates(),
/// preventing accidental in-place mutation of store-held references in concurrent scenarios.
/// </summary>
public record UserProfile(
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
    public UserProfile WithUpdates(string? risk = null, string? goals = null, string? timeframe = null)
    {
        return new UserProfile(
            UserId,
            !string.IsNullOrWhiteSpace(risk) ? risk : RiskTolerance,
            !string.IsNullOrWhiteSpace(goals) ? goals : InvestmentGoals,
            !string.IsNullOrWhiteSpace(timeframe) ? timeframe : InvestmentTimeframe
        );
    }
};