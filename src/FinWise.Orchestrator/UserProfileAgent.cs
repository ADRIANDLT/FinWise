using Microsoft.Extensions.AI;
using Serilog;
using System.ComponentModel;
using Microsoft.Agents.AI;

namespace FinWise.Orchestrator;

public class UserProfileAgent
{
    private readonly IChatClient _chatClient;
    private readonly IUserProfileStore _profileStore;

    public string Prompt =>
        $"""
        You are the User Profile Agent. Your job is to collect user profile information INCREMENTALLY.

        INCREMENTAL SAVING PATTERN:
        - Call set_profile() EACH TIME you receive new data (don't wait for all fields)
        - The profile saves progressively - you don't need all fields at once
        - When set_profile returns "COMPLETE", the profile has all required fields
        - When set_profile returns "PARTIAL", continue collecting the missing data

        Follow these steps:

        1. GET USER EMAIL
           - Check if the CURRENT user message IS an email address (contains '@')
           - If current message IS an email, use it and go to step 2
           - Otherwise: Look for email in previous conversation messages
           - No email found? Ask: 'Please provide your email address.'

        2. CHECK PROFILE STORE
           - CALL get_profile(email) to check store
           - Response 'FOUND_COMPLETE': Profile is complete! Output PROFILE_READY marker and answer their question
           - Response 'FOUND_PARTIAL': Profile exists but incomplete. Ask for the missing fields listed
           - Response 'NOT_FOUND': Create new profile by collecting data (step 3)

        3. COLLECT PROFILE DATA (one question at a time)
           Ask these questions IN ORDER. Accept any answer the user gives - these are free-form fields:

           A) Risk Tolerance: 'What is your risk tolerance? (e.g., conservative, moderate, aggressive, or describe in your own words)'
              -> When answered, call set_profile(email, user's answer, "", "")

           B) Investment Goals: 'What are your investment goals?'
              -> When answered, call set_profile(email, "", user's answer, "")

           C) Investment Timeframe: 'What is your investment timeframe? (e.g., short-term, 5 years, long-term, retirement in 20 years)'
              -> When answered, call set_profile(email, "", "", user's answer)

           After each set_profile call:
           - If returns "COMPLETE" -> Output PROFILE_READY marker and answer their question!
           - If returns "PARTIAL" -> Ask the next question for missing data

        4. OUTPUT PROFILE_READY MARKER
           When profile is complete, output exactly:
           'PROFILE_READY: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]'

           Then IMMEDIATELY answer their original question!

        IMPORTANT RULES:
        - Call set_profile() after receiving EACH piece of data - don't wait!
        - Always pass empty string "" for fields you don't have yet
        - Accept ANY answer the user gives - don't reject or re-ask for specific formats
        - The system handles merging new data with existing profile
        - Output PROFILE_READY only when set_profile returns "COMPLETE"
        - Ask questions ONE at a time

        EXAMPLE NEW USER FLOW:
        1. User: 'Give me financial advice' -> Ask for email
        2. User: 'test@example.com' -> Call get_profile() -> "NOT_FOUND" -> Ask for risk tolerance
        3. User: 'I'm pretty cautious with money' -> Call set_profile("test@example.com", "I'm pretty cautious with money", "", "") -> "PARTIAL" -> Ask for goals
        4. User: 'Save for retirement and my kids college' -> Call set_profile("test@example.com", "", "Save for retirement and my kids college", "") -> "PARTIAL" -> Ask for timeframe
        5. User: 'About 15-20 years' -> Call set_profile("test@example.com", "", "", "About 15-20 years") -> "COMPLETE" -> Output PROFILE_READY -> Answer their question!

        RETURNING USER FLOW:
        1. User: 'What stocks should I buy?' -> Find email in conversation -> Call get_profile()
        2. get_profile returns "FOUND_COMPLETE: ..." -> Output PROFILE_READY -> Answer their question!

        TOOLS:
        - get_profile(email) - Check if profile exists. Returns FOUND_COMPLETE, FOUND_PARTIAL, or NOT_FOUND
        - set_profile(email, risk, goals, timeframe) - Save/update profile. Pass "" for unknown fields. Returns COMPLETE or PARTIAL
        - delete_profile(email) - Permanently delete a user profile. Returns DELETED, NOT_FOUND, or ERROR.

        DELETE PROFILE FLOW:
        1. User asks to delete their profile (e.g., 'Delete my profile', 'Remove my data')
        2. Verify which email address to delete — ask the user to confirm before proceeding with deletion
        3. Call delete_profile(email)
        4. Inform the user of the result: DELETED (success), NOT_FOUND (no profile), or ERROR

        EXAMPLE DELETE FLOW:
        1. User: 'Delete my profile' -> Check if email is known from conversation -> If not, ask for email
        2. User: 'john@email.com' -> Confirm: 'Are you sure you want to permanently delete the profile for john@email.com?'
        3. User: 'Yes' -> Call delete_profile("john@email.com") -> "DELETED: ..." -> Inform the user their profile has been deleted

        ANSWERING QUESTIONS (after PROFILE_READY):
        - Use the profile data to give personalized advice
        - More cautious/conservative profiles: suggest bonds, CDs, stable investments
        - More aggressive profiles: suggest growth stocks, ETFs, higher-risk investments
        - Always include disclaimer about consulting a licensed financial advisor
        """;

    public string Instructions => Prompt;
    public string Name => "profile_agent";
    public string Description => "Collects user profile information";

    public UserProfileAgent(IChatClient chatClient, IUserProfileStore profileStore)
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

        if (string.IsNullOrWhiteSpace(userId) || !userId.Contains('@'))
        {
            Log.Warning("GetProfile: Invalid userId (email) provided: {UserId}", userId);
            return "ERROR: Valid email address required";
        }

        var profile = await _profileStore.GetProfileAsync(userId);
        if (profile != null)
        {
            Log.Information("Profile FOUND for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}, IsComplete={IsComplete}",
                userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe, profile.IsComplete);

            if (profile.IsComplete)
            {
                return $"FOUND_COMPLETE: email={userId} risk={profile.RiskTolerance} goals={profile.InvestmentGoals} timeframe={profile.InvestmentTimeframe}";
            }
            else
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(profile.RiskTolerance)) missing.Add("risk tolerance");
                if (string.IsNullOrWhiteSpace(profile.InvestmentGoals)) missing.Add("investment goals");
                if (string.IsNullOrWhiteSpace(profile.InvestmentTimeframe)) missing.Add("timeframe");
                return $"FOUND_PARTIAL: email={userId} risk={profile.RiskTolerance ?? "missing"} goals={profile.InvestmentGoals ?? "missing"} timeframe={profile.InvestmentTimeframe ?? "missing"}. Still need: {string.Join(", ", missing)}";
            }
        }
        else
        {
            Log.Warning("Profile NOT FOUND for {UserId}", userId);
            return $"NOT_FOUND: No profile exists for {userId}. Create one by calling set_profile with the email.";
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

        if (string.IsNullOrWhiteSpace(userId) || !userId.Contains('@'))
        {
            Log.Warning("SetProfile: Invalid userId (email) provided: {UserId}", userId);
            return "ERROR: Valid email address required";
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
            UserProfileDto profile;
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
                profile = new UserProfileDto(userId, riskValue, goalsValue, timeframeValue);
                Log.Information("Profile CREATED for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}",
                    userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe);
            }

            await _profileStore.SetProfileAsync(userId, profile);

            if (profile.IsComplete)
            {
                Log.Information("Profile COMPLETE for {UserId}", userId);
                return $"COMPLETE: Profile saved with all fields. Risk={profile.RiskTolerance}, Goals={profile.InvestmentGoals}, Timeframe={profile.InvestmentTimeframe}";
            }
            else
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(profile.RiskTolerance)) missing.Add("risk tolerance");
                if (string.IsNullOrWhiteSpace(profile.InvestmentGoals)) missing.Add("investment goals");
                if (string.IsNullOrWhiteSpace(profile.InvestmentTimeframe)) missing.Add("timeframe");
                Log.Information("Profile PARTIAL for {UserId}, missing: {Missing}", userId, string.Join(", ", missing));
                return $"PARTIAL: Profile saved. Still need: {string.Join(", ", missing)}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetProfile failed for {UserId}", userId);
            return "ERROR: An unexpected error occurred while saving the profile. Please try again.";
        }
    }

    /// <summary>
    /// Tool: Delete a user profile permanently by email.
    /// </summary>
    [Description("Delete a user profile permanently by email. Returns confirmation or error.")]
    public async Task<string> DeleteProfile([Description("The user's email address")] string userId)
    {
        Log.Information("profileAgent tool: DeleteProfile called for {UserId}", userId);

        if (string.IsNullOrWhiteSpace(userId) || !userId.Contains('@'))
        {
            Log.Warning("DeleteProfile: Invalid userId (email) provided: {UserId}", userId);
            return "ERROR: Valid email address required";
        }

        try
        {
            var existing = await _profileStore.GetProfileAsync(userId);
            if (existing == null)
            {
                Log.Warning("DeleteProfile: Profile not found for {UserId}", userId);
                return $"NOT_FOUND: No profile exists for {userId}.";
            }

            await _profileStore.DeleteProfileAsync(userId);
            Log.Information("Profile DELETED for {UserId}", userId);
            return $"DELETED: Profile for {userId} has been permanently deleted.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DeleteProfile failed for {UserId}", userId);
            return "ERROR: An unexpected error occurred while deleting the profile. Please try again.";
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
