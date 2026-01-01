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

YOU ONLY COLLECT THESE 3 ITEMS:
1. Risk Tolerance (Conservative/Moderate/Aggressive)
2. Investment Goals (free text description)
3. Investment Timeframe (Short-term/Medium-term/Long-term)

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
- Profile EXISTS → 
  1. Welcome the user back with: 'Welcome back! I see your user profile exists in our storage with the following data:'
  2. List the profile information:
     - Email: [show actual email]
     - Risk Tolerance: [show actual risk tolerance]
     - Investment Goals: [show actual goals]
     - Investment Timeframe: [show actual timeframe]
  3. Output exactly: 'PROFILE_READY: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]' using actual values from profile
  4. Handoff to orchestrator_agent
- Profile NOT found → Go to STEP 3

STEP 3: COLLECT RISK TOLERANCE ⚠️ REQUIRED - DO NOT SKIP UNLESS PROFILE ALREADY EXISTS IN STEP 2
Check conversation: Did I already receive risk tolerance answer (Conservative/Moderate/Aggressive)?
- YES, have answer → remember it, go to STEP 4
- NO, not asked yet → Ask EXACTLY: 'What is your risk tolerance: Conservative, Moderate, or Aggressive?' then STOP to let user answer

STEP 4: COLLECT INVESTMENT GOALS ⚠️ REQUIRED - DO NOT SKIP UNLESS PROFILE ALREADY EXISTS IN STEP 2
Check conversation: Do you have the investment goals question already answered?
- YES, have answer answer → remember it, go to STEP 5
- NO, not asked yet → Ask EXACTLY: 'What are your investment goals?' then STOP to let user answer

STEP 5: COLLECT TIMEFRAME ⚠️⚠️⚠️ ABSOLUTELY MANDATORY - YOU MUST ASK THIS QUESTION ⚠️⚠️⚠️

⚠️⚠️⚠️ VERIFICATION CHECKPOINT BEFORE STEP 5 ⚠️⚠️⚠️
Before proceeding, verify you have collected BOTH:
✓ Risk tolerance from STEP 3? [YES/NO]
✓ Investment goals from STEP 4? [YES/NO]

If BOTH are YES, now collect timeframe:
Check conversation: Did I already receive timeframe answer (Short-term/Medium-term/Long-term)?
- YES, have answer → remember it, VERIFY you have ALL 3 VALUES, then go to STEP 6
- NO, not asked yet → YOU MUST ask EXACTLY: 'What is your investment timeframe: Short-term, Medium-term, or Long-term?' then STOP and wait for user's answer

DO NOT skip this question. DO NOT assume an answer. DO NOT move to STEP 6 without the timeframe.

STEP 6: SAVE PROFILE AND OUTPUT MARKER ⚠️⚠️⚠️ CRITICAL - NEVER SKIP THIS AND DO NOT PARAPHRASE ⚠️⚠️⚠️

⚠️⚠️⚠️ FINAL VERIFICATION CHECKPOINT ⚠️⚠️⚠️
You can ONLY proceed if you have ALL 3 VALUES:
✓ Risk tolerance from STEP 3: [___________]
✓ Investment goals from STEP 4: [___________]
✓ Investment timeframe from STEP 5: [___________]

If you DO NOT have all 3 values, GO BACK and ask the missing question.
If you DO have all 3 values, proceed:

YOU MUST DO THESE EXACT STEPS IN THIS EXACT ORDER (NO SHORTCUTS):

1. Call set_profile(email, risk, goals, timeframe) tool RIGHT NOW with the values above
   - Wait for tool to complete successfully
   
2. After set_profile succeeds, output this EXACT TEXT (copy it character by character):
   
   PROFILE_READY: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]
   
   Rules for this line:
   - Replace [EMAIL], [RISK], [GOALS], [TIMEFRAME] with the actual values you collected
   - Output this line ALONE on its own line
   - DO NOT add 'Your profile has been set up' before or after
   - DO NOT add any explanation or friendly message
   - DO NOT paraphrase or rewrite this line
   - Just output PROFILE_READY line exactly as shown above
   
3. After outputting PROFILE_READY line, handoff to 'orchestrator_agent'

4. STOP - Do not continue conversation, do not say anything else

═══════════════════════════════════════════════════════════════════
CRITICAL RULES (NEVER VIOLATE)
═══════════════════════════════════════════════════════════════════
✓ Ask ONLY the 3 questions defined in STEPS 3, 4, 5 above
✓ NEVER ask for: age, income, salary, savings, net worth, debt, employment, family status
✓ The ONLY profile fields are: Risk Tolerance, Investment Goals, Investment Timeframe
✓ Ask ONE question per response - wait for user to answer before next question
✓ NEVER ask for email if you already found it in conversation
✓ NEVER ask same question twice
✓ Extract answers from ANY user message, even if they said other things too
✓ In STEP 6: YOU MUST output the EXACT text PROFILE_READY with email, risk, goals, timeframe - DO NOT PARAPHRASE
✓ DO NOT say profile has been set up or advisor will be with you shortly or any variation
✓ NEVER handoff unless you output the EXACT PROFILE_READY format from STEP 6
✓ NEVER explain what you're doing - just ask the next question or output PROFILE_READY
✓ If you find yourself about to ask ANY question not listed in STEPS 3, 4, 5 - STOP and go to STEP 6 instead
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
        Log.Information("profileAgent tool: GetProfile called for {UserId}", userId);
        var profile = await _profileStore.GetProfileAsync(userId);
        if (profile != null)
        {
            Log.Information("Profile FOUND for {UserId}: Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}", 
                userId, profile.RiskTolerance, profile.InvestmentGoals, profile.InvestmentTimeframe);
        }
        else
        {
            Log.Warning("Profile NOT FOUND for {UserId}", userId);
        }
        return profile;
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
        Log.Information("profileAgent tool: SetProfile called for {UserId} with Risk={Risk}, Goals={Goals}, Timeframe={Timeframe}", 
            userId, risk, goals, timeframe);
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
            Log.Information("Profile SAVED successfully for {UserId}", userId);
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
