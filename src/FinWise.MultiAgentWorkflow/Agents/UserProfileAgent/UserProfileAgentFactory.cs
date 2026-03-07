using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore;
using Microsoft.Extensions.AI;
using Serilog;
using System.ComponentModel;
using Microsoft.Agents.AI;

namespace FinWise.MultiAgentWorkflow.Agents.UserProfileAgent;

public class UserProfileAgentFactory
{
    // Profile tool response prefixes (interpreted by the LLM agent)
    private const string ResponseFoundComplete = "FOUND_COMPLETE:";
    private const string ResponseFoundPartial = "FOUND_PARTIAL:";
    private const string ResponseNotFound = "NOT_FOUND:";
    private const string ResponseComplete = "COMPLETE:";
    private const string ResponsePartial = "PARTIAL:";
    private const string ResponseDeleted = "DELETED:";
    private const string ResponseError = "ERROR:";

    private readonly IChatClient _chatClient;
    private readonly IUserProfileStore _profileStore;

    public string Prompt { get; } = LoadPrompt();

    // Prompt loaded from embedded .prompt.md resource for editor-friendly editing,
    // clean git diffs on prompt changes, and separation of prompt engineering from C# code.
    private static string LoadPrompt()
    {
        var assembly = typeof(UserProfileAgentFactory).Assembly;
        const string resourceName = "FinWise.MultiAgentWorkflow.Agents.UserProfileAgent.UserProfileAgent.prompt.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static bool IsValidEmail(string? email)
    {
        return !string.IsNullOrWhiteSpace(email) && email.Contains('@');
    }

    public string Instructions => Prompt;
    public string Name => "profile_agent";
    public string Description => "Collects user profile information";

    public UserProfileAgentFactory(IChatClient chatClient, IUserProfileStore profileStore)
    {
        _chatClient = chatClient;
        _profileStore = profileStore;
    }

    /// <summary>
    /// Tool: Get user profile by userId. Returns profile with completion status.
    /// </summary>
    [Description("Get user profile by email. Returns the profile if found, with completion status (COMPLETE or PARTIAL). Use this to check if a returning user has a profile.")]
    public async Task<string> GetProfile([Description("The user's email address")] string userId)
    {
        Log.Information("profileAgent tool: GetProfile called for {UserId}", userId);

        if (!IsValidEmail(userId))
        {
            Log.Warning("GetProfile: Invalid userId (email) provided: {UserId}", userId);
            return $"{ResponseError} Valid email address required";
        }

        var profile = await _profileStore.GetProfileAsync(userId);
        if (profile != null)
        {
            Log.Information("Profile FOUND for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}, IsComplete={IsComplete}",
                userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe, profile.IsComplete);

            if (profile.IsComplete)
            {
                return $"{ResponseFoundComplete} email={userId} risk={profile.RiskTolerance} goals={profile.InvestmentGoals} timeframe={profile.InvestmentTimeframe}";
            }
            else
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(profile.RiskTolerance)) missing.Add("risk tolerance");
                if (string.IsNullOrWhiteSpace(profile.InvestmentGoals)) missing.Add("investment goals");
                if (string.IsNullOrWhiteSpace(profile.InvestmentTimeframe)) missing.Add("timeframe");
                return $"{ResponseFoundPartial} email={userId} risk={profile.RiskTolerance ?? "missing"} goals={profile.InvestmentGoals ?? "missing"} timeframe={profile.InvestmentTimeframe ?? "missing"}. Still need: {string.Join(", ", missing)}";
            }
        }
        else
        {
            Log.Warning("Profile NOT FOUND for {UserId}", userId);
            return $"{ResponseNotFound} No profile exists for {userId}. Create one by calling set_profile with the email.";
        }
    }

    /// <summary>
    /// Tool: Set/update user profile incrementally. Call this each time you receive new profile data.
    /// All fields are free-form text - just context for the advisor agent.
    /// </summary>
    [Description("Set or update user profile. Call this EACH TIME you receive new data. All fields accept free-form text. Pass empty string for fields not yet provided. Returns COMPLETE when all fields are filled, or PARTIAL if still collecting.")]
    public async Task<string> SetProfile(
        [Description("The user's email address (required)")] string userId,
        [Description("User's risk tolerance (e.g., 'conservative', 'moderate', 'aggressive', or any description) - pass empty string if not yet provided")] string risk = "",
        [Description("User's investment goals (any text describing their financial goals) - pass empty string if not yet provided")] string goals = "",
        [Description("User's investment timeframe (e.g., 'short-term', '5 years', 'long-term', or any description) - pass empty string if not yet provided")] string timeframe = "")
    {
        Log.Information("profileAgent tool: SetProfile called for {UserId} with Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}",
            userId, risk, goals, timeframe);

        if (!IsValidEmail(userId))
        {
            Log.Warning("SetProfile: Invalid userId (email) provided: {UserId}", userId);
            return $"{ResponseError} Valid email address required";
        }

        try
        {
            // Get existing profile if any (for incremental updates)
            var existingProfile = await _profileStore.GetProfileAsync(userId);

            // Just use the values as-is (free-form text)
            string? riskValue = !string.IsNullOrWhiteSpace(risk) ? risk.Trim() : null;
            string? goalsValue = !string.IsNullOrWhiteSpace(goals) ? goals.Trim() : null;
            string? timeframeValue = !string.IsNullOrWhiteSpace(timeframe) ? timeframe.Trim() : null;

            // Create or update profile
            UserProfile profile;
            if (existingProfile != null)
            {
                // Update existing profile with new values (preserve existing values for fields not provided)
                profile = existingProfile.WithUpdates(
                    risk: riskValue,
                    goals: goalsValue,
                    timeframe: timeframeValue
                );
                Log.Information("Profile UPDATED for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}",
                    userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe);
            }
            else
            {
                // Create new profile with whatever data we have
                profile = new UserProfile(userId, riskValue, goalsValue, timeframeValue);
                Log.Information("Profile CREATED for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}",
                    userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe);
            }

            await _profileStore.SetProfileAsync(userId, profile);

            if (profile.IsComplete)
            {
                Log.Information("Profile COMPLETE for {UserId}", userId);
                return $"{ResponseComplete} Profile saved with all fields. Risk={profile.RiskTolerance}, Goals={profile.InvestmentGoals}, Timeframe={profile.InvestmentTimeframe}";
            }
            else
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(profile.RiskTolerance)) missing.Add("risk tolerance");
                if (string.IsNullOrWhiteSpace(profile.InvestmentGoals)) missing.Add("investment goals");
                if (string.IsNullOrWhiteSpace(profile.InvestmentTimeframe)) missing.Add("timeframe");
                Log.Information("Profile PARTIAL for {UserId}, missing: {Missing}", userId, string.Join(", ", missing));
                return $"{ResponsePartial} Profile saved. Still need: {string.Join(", ", missing)}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetProfile failed for {UserId}", userId);
            return $"{ResponseError} An unexpected error occurred while saving the profile. Please try again.";
        }
    }

    /// <summary>
    /// Tool: Delete a user profile permanently by email.
    /// </summary>
    [Description("Delete a user profile permanently by email. Returns confirmation or error.")]
    public async Task<string> DeleteProfile([Description("The user's email address")] string userId)
    {
        Log.Information("profileAgent tool: DeleteProfile called for {UserId}", userId);

        if (!IsValidEmail(userId))
        {
            Log.Warning("DeleteProfile: Invalid userId (email) provided: {UserId}", userId);
            return $"{ResponseError} Valid email address required";
        }

        try
        {
            var existing = await _profileStore.GetProfileAsync(userId);
            if (existing == null)
            {
                Log.Warning("DeleteProfile: Profile not found for {UserId}", userId);
                return $"{ResponseNotFound} No profile exists for {userId}.";
            }

            await _profileStore.DeleteProfileAsync(userId);
            Log.Information("Profile DELETED for {UserId}", userId);
            return $"{ResponseDeleted} Profile for {userId} has been permanently deleted.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DeleteProfile failed for {UserId}", userId);
            return $"{ResponseError} An unexpected error occurred while deleting the profile. Please try again.";
        }
    }

    /// <summary>
    /// Creates a ChatClientAgent with the profile agent's prompt and tools.
    /// </summary>
    public ChatClientAgent CreateAgent()
    {
        var tools = new AIFunction[]
        {
            AIFunctionFactory.Create(GetProfile),
            AIFunctionFactory.Create(SetProfile),
            AIFunctionFactory.Create(DeleteProfile)
        };
        return new ChatClientAgent(_chatClient, Prompt, Name, Description, tools: tools);
    }
}
