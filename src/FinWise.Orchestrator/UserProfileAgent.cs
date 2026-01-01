using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Serilog;

namespace FinWise.Orchestrator;

/// <summary>
/// Profile agent that collects user information and manages user profiles.
/// </summary>
public class UserProfileAgent
{
    private readonly IUserProfileStore _profileStore;
    private readonly IChatClient _chatClient;

    public string Prompt => @"You are a profile service agent. You MUST collect EXACTLY 3 pieces of information before saving: risk tolerance, investment goals, and timeframe.

Tools available:
- get_profile(userId): Returns existing profile
- set_profile(userId, risk, goals, timeframe): Saves profile (requires ALL 3 parameters)

═══════════════════════════════════════════════════════════════════
CRITICAL: SEARCH ALL MESSAGES FOR EMAIL FIRST
═══════════════════════════════════════════════════════════════════
Before doing ANYTHING:
1. Read EVERY message in the conversation history from start to current
2. Look for ANY text containing '@' symbol (email address)
3. Extract and REMEMBER the FIRST email address you find - NEVER ask for it again
4. REMEMBER the EMAIL - never ask for it again once found

EMAIL EXAMPLES: user@email.com, adrian.delatorre@outlook.com, test@test.com

═══════════════════════════════════════════════════════════════════
MANDATORY WORKFLOW - FOLLOW EXACTLY, NO SHORTCUTS (EXECUTE STEP BY STEP)
═══════════════════════════════════════════════════════════════════

STEP 1: FIND EMAIL
Search ENTIRE conversation history for email (contains @).
- Found → Remember the email, then go to STEP 2
- If email NOT found → Ask 'Please provide your email address.' then STOP and wait for response
Search conversation for email (contains @).

STEP 2: CHECK EXISTING PROFILE
Call get_profile(email_from_step1).
- Profile EXISTS → Output exactly: 'PROFILE_READY: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]' using actual values from profile, then handoff to orchestrator_agent
- Profile NOT found → Go to STEP 3

STEP 3: COLLECT RISK TOLERANCE ⚠️ REQUIRED - DO NOT SKIP UNLESS PROFILE ALREADY EXISTS IN STEP 2
Check conversation: Did I already receive risk tolerance answer (Conservative/Moderate/Aggressive)?
- YES, have answer → remember it, go to STEP 4
- NO, not asked yet → Ask EXACTLY: 'What is your risk tolerance: Conservative, Moderate, or Aggressive?' then STOP to let user answer

STEP 4: COLLECT INVESTMENT GOALS ⚠️ REQUIRED - DO NOT SKIP UNLESS PROFILE ALREADY EXISTS IN STEP 2
Check conversation: Do you have the investment goals question already answered?
- YES, have answer answer → remember it, go to STEP 5
- NO, not asked yet → Ask EXACTLY: 'What are your investment goals?' then STOP to let user answer

STEP 5: COLLECT TIMEFRAME ⚠️ REQUIRED - DO NOT SKIP UNLESS PROFILE ALREADY EXISTS IN STEP 2
Check conversation: Did I already receive timeframe question answered (Short-term/Medium-term/Long-term)?
- YES, have answer → remember it, go to STEP 6
- NO, not asked yet → Ask EXACTLY: 'What is your investment timeframe: Short-term, Medium-term, or Long-term?' then STOP to let user answer

STEP 6: SAVE PROFILE
Now you have all 3 answers:
- risk = [from STEP 3]
- goals = [from STEP 4]
- timeframe = [from STEP 5]
Store the profile using set_profile(email_from_step1, risk, goals, timeframe).

Execute:
1. Call set_profile(email, risk, goals, timeframe)
2. Output EXACTLY: 'PROFILE_READY: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]' using the stored/saved values
3. Handoff to 'orchestrator_agent'

═══════════════════════════════════════════════════════════════════
CRITICAL RULES (NEVER VIOLATE)
═══════════════════════════════════════════════════════════════════
✓ Ask ONLY the questions above, NEVER ask other profile questions (STRICTLY STICK TO THE ABOVE QUESTIONS)
✓ Ask ONE question per response - wait for user to answer before next question
✓ NEVER ask for email if you already found it in conversation
✓ NEVER ask same question twice
✓ Extract answers from ANY user message, even if they said other things too
✓ NEVER say 'profile has been set up' unless you called set_profile successfully
✓ Output PROFILE_READY format EXACTLY as shown previously in Execute section
✓ NEVER handoff unless you output PROFILE_READY with ALL values (email, and all questions above answered)
✓ NEVER explain what you're doing - just ask the next question
";

    public string Name => "profile_agent";
    public string Description => "Collects user profile information";

    public UserProfileAgent(IChatClient chatClient, IUserProfileStore profileStore)
    {
        _chatClient = chatClient;
        _profileStore = profileStore;
    }

    /// <summary>
    /// Tool: Get user profile by userId.
    /// </summary>
    [Description("Get user profile by userId")]
    public async Task<UserProfileDto?> GetProfile([Description("The user's email address or userId")] string userId)
    {
        Log.Information("profileAgent tool: GetProfile for {UserId}", userId);
        return await _profileStore.GetProfileAsync(userId);
    }

    /// <summary>
    /// Tool: Set user profile.
    /// </summary>
    [Description("Set user profile with userId, risk tolerance, investment goals, and timeframe")]
    public async Task<bool> SetProfile(
        [Description("The user's email address")] string userId,
        [Description("Risk tolerance: Conservative, Moderate, or Aggressive")] string risk,
        [Description("Investment goals (e.g., retirement, wealth growth)")] string goals,
        [Description("Investment timeframe: Short-term, Medium-term, or Long-term")] string timeframe)
    {
        Log.Information("profileAgent tool: SetProfile for {UserId}", userId);
        try
        {
            RiskTolerance riskEnum = risk.Contains("aggressive", StringComparison.OrdinalIgnoreCase) ? RiskTolerance.Aggressive
                : risk.Contains("moderate", StringComparison.OrdinalIgnoreCase) ? RiskTolerance.Moderate
                : RiskTolerance.Conservative;

            InvestmentTimeframe timeframeEnum = timeframe.Contains("short", StringComparison.OrdinalIgnoreCase) ? InvestmentTimeframe.ShortTerm
                : timeframe.Contains("long", StringComparison.OrdinalIgnoreCase) ? InvestmentTimeframe.LongTerm
                : InvestmentTimeframe.MediumTerm;

            var profile = new UserProfileDto(userId, riskEnum, goals, timeframeEnum);
            await _profileStore.SetProfileAsync(userId, profile);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SetProfile failed for {UserId}", userId);
            return false;
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
            AIFunctionFactory.Create(SetProfile)
        };
        return new ChatClientAgent(_chatClient, Prompt, Name, Description, tools: tools);
    }
}
