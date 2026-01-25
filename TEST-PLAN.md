# Test Plan: Profile Capture Fix Validation

## Background
Fixed critical bug where profile agent wasn't consistently outputting the PROFILE_COMPLETE format, causing profiles to never be stored.

## Changes Made
1. **Strengthened Profile Agent Instructions** - Added explicit IMPORTANT/MUST requirements about outputting the structured format
2. **Improved Parsing Logic** - Changed from checking only last agent ID to searching ALL workflow messages for PROFILE_COMPLETE marker

## Test Scenarios

### Test 1: New User Profile Collection
**Objective:** Verify profile is collected, parsed, and stored correctly

**Steps:**
1. Restart the MCP server (to ensure fresh profileStore)
2. Send query: "I need investment advice"
3. Provide user ID when asked (e.g., "delatorre@outlook.com")
4. Answer profile questions:
   - Risk tolerance: "Moderate"
   - Investment goals: "Retirement planning and wealth growth"
   - Time horizon: "Long-term, 20-25 years"

**Expected Results:**
- ✅ Profile agent asks all 4 questions
- ✅ **Check logs** for PROFILE_COMPLETE format appearing in workflow outputs
- ✅ Advisor provides investment recommendations
- ✅ profileStore contains entry for "delatorre@outlook.com"

**Validation in Logs:**
Look for line containing:
```
PROFILE_COMPLETE|UserID:delatorre@outlook.com|Risk:Moderate|Goals:Retirement planning and wealth growth|Timeframe:Long-term
```

### Test 2: Returning User (Profile Reuse)
**Objective:** Verify stored profile is loaded and questions are skipped

**Prerequisites:** Test 1 completed successfully

**Steps:**
1. Send new query: "Hi, my email is delatorre@outlook.com and I need more advice"
2. Observe response

**Expected Results:**
- ✅ System detects existing profile for delatorre@outlook.com
- ✅ **Profile questions are SKIPPED** (goes directly to advisor)
- ✅ Advisor response includes context from stored profile
- ✅ Logs show "Found existing profile for user: delatorre@outlook.com"

### Test 3: Different User
**Objective:** Verify system handles multiple user profiles

**Steps:**
1. Send query: "I need financial advice, my name is Sarah Johnson"
2. Provide profile information when asked

**Expected Results:**
- ✅ System treats as new user (sarah profile not in store)
- ✅ Profile agent collects all 4 data points
- ✅ PROFILE_COMPLETE format captured in logs
- ✅ profileStore now contains 2 entries (delatorre + sarah)

## Critical Checks

### Log Verification
After each test, check `Console Output` for:

1. **Profile Detection:**
   ```
   Checking for existing profile for user: <userId>
   ```

2. **Profile Capture (Test 1 & 3):**
   ```
   PROFILE_COMPLETE|UserID:...|Risk:...|Goals:...|Timeframe:...
   ```

3. **Profile Storage:**
   ```
   Stored profile for user: <userId>
   ```

4. **Profile Reuse (Test 2):**
   ```
   Found existing profile for user: <userId>
   Injecting profile into conversation context
   ```

### Known Issue If Tests Fail
If PROFILE_COMPLETE marker still doesn't appear:
- The LLM may not be following instructions despite strengthened prompts
- May need alternative approach (e.g., structured output via JSON mode, or manual parsing of conversation turns)

## Success Criteria
- [ ] Test 1: New profile captured and stored
- [ ] Test 2: Returning user skips profile questions
- [ ] Test 3: Multiple users handled independently
- [ ] All PROFILE_COMPLETE markers appear in logs when expected
- [ ] No profile questions asked when profile exists
