# Bug Fix: New Session Email Requirement

## Problem
When a user starts a new conversation/session in GitHub Copilot or any MCP client, the FinWise system provided financial advice without asking for user identification (email) first. This violates the requirement that **every new conversation must start by identifying the user via email**.

## Root Cause
The system architecture has:
1. **Profiles stored by email** in `InMemoryUserProfileStore` (persists across ALL conversations for the entire MCP server process lifetime)
2. **Conversations stored by conversationId** in `InMemoryConversationStore` (separate history per MCP client conversation)
3. **Conversation detection** based on empty message history

However, the ProfileAgent's logic wasn't strict enough about checking for the PROFILE_READY marker before determining if a user is identified. The agent would search for any email in conversation history, but in a truly new conversation with empty history, this should always fail and prompt for email.

## The Bug
Looking at the logs (`finwise-20260103 - Night.log`), the system DID correctly ask for email in the logged conversation. But the user experienced a scenario where advice was provided without email collection, suggesting:
- Either conversation state was being reused improperly
- Or the orchestrator wasn't routing to profile_agent first in a new conversation
- Or the profile_agent logic had an edge case where it didn't ask for email

## Solution
The fix requires two changes:

### 1. **ProfileAgent: Enforce PROFILE_READY Check**
The ProfileAgent prompt must be updated to:
- **FIRST** check if `PROFILE_READY:` marker exists in conversation history
- If NO marker found → This is a new/unidentified conversation → ASK FOR EMAIL IMMEDIATELY
- If marker found → User already identified, proceed with profile management

**Key Change**: Instead of just searching for any email in history, check for the PROFILE_READY marker which is only outputted AFTER profile is confirmed (loaded or created).

### 2. **Orchestrator: Strict PROFILE_READY Enforcement**  
The orchestrator prompt must be updated to:
- **ALWAYS** check for `PROFILE_READY:` marker first before any routing decision
- If NO marker → **MUST** route to profile_agent (zero exceptions, even if user asks for advice)
- If marker found → Route based on user intent (profile management vs. financial advice)

## Implementation Notes

The fix should be minimal and focused on the prompt logic, NOT code changes. The architecture is correct:

1. **Profiles keyed by email** - correct (enables reuse across conversations)
2. **Conversations keyed by conversationId** - correct (separate history per chat session)
3. **ConversationManager detects new conversations** - correct (checks for empty history)

The issue is purely in the **agent prompt logic** for determining when to ask for email.

## Testing the Fix

After implementing, test these scenarios:

1. **New session, no history**: Ask "Give me financial advice" → Should ask for email first
2. **Returning user, existing profile**: Provide email → Should load profile from store
3. **New user, no profile**: Provide email → Should collect profile step-by-step
4. **Continue existing conversation**: Advice request in same conversation → Should provide advice (PROFILE_READY exists)

## Files to Update

1. `src/FinWise.Orchestrator/UserProfileAgent.cs` - Update the `Prompt` property
2. `src/FinWise.Orchestrator/Program.cs` - Update the orchestratorAgent prompt

**Note**: Changes should be minimal, surgical edits to the prompt strings only. No architectural changes needed.

## Expected Behavior After Fix

**New Session Flow**:
```
User: "Give me financial advice"
System: "Please provide your email address."

User: "john@example.com"  
System: [Checks profile store]
  - If found: "Welcome back! [shows profile] PROFILE_READY: ..."
  - If not found: "What is your risk tolerance: Conservative, Moderate, or Aggressive?"

[After profile complete]
System: "PROFILE_READY: email=john@example.com ..."

User: "Give me investment advice"
System: [Provides personalized advice based on profile]
```

**Key Point**: Every new conversation (empty history, no PROFILE_READY marker) MUST start with "Please provide your email address."
