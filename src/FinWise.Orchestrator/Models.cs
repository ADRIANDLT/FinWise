namespace FinWise.Orchestrator;

/// <summary>
/// User profile data transfer object for in-memory storage
/// </summary>
public record UserProfileDto(
    string UserIdentifier,
    RiskTolerance RiskTolerance,
    string InvestmentGoals,
    InvestmentTimeframe InvestmentTimeframe
);

/// <summary>
/// Risk tolerance enumeration
/// </summary>
public enum RiskTolerance
{
    Conservative,
    Moderate,
    Aggressive
}

/// <summary>
/// Investment timeframe enumeration
/// </summary>
public enum InvestmentTimeframe
{
    ShortTerm,    // < 3 years
    MediumTerm,   // 3-10 years
    LongTerm      // > 10 years
}

/// <summary>
/// Workflow execution context for multi-agent orchestration
/// </summary>
public record WorkflowExecutionContext(
    string UserIdentifier,
    string Query,
    DateTime RequestTime
);
