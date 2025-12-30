# Quickstart Guide: FinWise v0.1 Multi-Agent Workflow

**Version**: 0.1.0  
**Target Audience**: Developers, testers, early adopters  
**Estimated Setup Time**: 30 minutes (Implementation Phase 2 with persistence) | 15 minutes (Implementation Phase 1 POC only)

## Overview

FinWise v0.1 is a multi-agent AI investment advisor accessible through AI assistants like Claude Desktop, ChatGPT, or GitHub Copilot. It orchestrates three specialized agents (Orchestrator, User Profile, Global Advisor) to provide personalized investment recommendations through natural conversation.

**Key Capabilities (v0.1 Baseline)**:
- Multi-agent workflow with dynamic routing and context-preserving handoffs
- User profile management (risk tolerance, investment goals, timeframe)
- Generic investment advice (stock vs. real estate guidance - "hollow" implementation)
- Agent escalation and navigation (return to previous agents, update profile mid-conversation)
- Accessible via MCP STDIO protocol (no custom UI required)
- **Persistent user profiles** (Implementation Phase 2 - recommended for all users)

**What's NOT in v0.1** (deferred to later versions):
- Detailed stock fundamentals or real estate market data
- Persistent conversation history (in-memory only)
- Multi-user support or authentication

**Setup Paths**:
- ✅ **Implementation Phase 2 Setup (RECOMMENDED)**: Full persistent user profiles with PostgreSQL - follow all steps below
- ⚠️ **Implementation Phase 1 Setup (POC only)**: In-memory only (profiles lost on restart) - skip PostgreSQL steps (temporary option for quick testing, **will be removed in future releases**)

**Which path should you choose?**
- **Use Implementation Phase 2** if you want to test realistic behavior (profiles persist across restarts, multi-process architecture)
- **Use Implementation Phase 1** only for quick initial testing or if you cannot install Docker (profile data is lost when process stops)

---

## Prerequisites

Before you begin, ensure you have:

- **Windows 11** (development workstation)
- **.NET 10 SDK** installed ([download](https://dotnet.microsoft.com/download))
- **Docker Desktop for Windows** ([download](https://www.docker.com/products/docker-desktop)) - **Required for Implementation Phase 2** (persistent profiles)
- **Azure OpenAI Service API key** (free tier acceptable)
  - Create resource: [Azure Portal → Create Azure OpenAI Service](https://portal.azure.com/#create/Microsoft.CognitiveServicesOpenAI)
  - Deploy model: `gpt-4o-mini` (recommended for cost-effective agents)
- **VS Code** with the following extensions:
  - **GitHub Copilot** (with MCP support) - Recommended for v0.1
  - **C# Dev Kit** (for development)

> **Note**: If you're doing Implementation Phase 1 POC only (in-memory profiles), you can skip Docker Desktop installation. However, **Implementation Phase 2 with persistent profiles is recommended for all users** as Implementation Phase 1 will be removed in future releases.

---

## Installation

### Step 1: Clone Repository and Install Dependencies

```powershell
# Clone FinWise repository
git clone https://github.com/your-org/finwise-orchestrator-mcp.git
cd finwise-orchestrator-mcp

# Checkout v0.1 feature branch
git checkout feature/orchestrator-mcp

# Restore NuGet packages
dotnet restore

# Verify .NET version
dotnet --version
# Expected: 10.0.x
```

### Step 2: Start PostgreSQL Database (Implementation Phase 2 - Persistent Profiles)

> **Skip this step if doing Implementation Phase 1 POC only** (in-memory profiles). Implementation Phase 2 with persistent profiles is the recommended setup path.

```powershell
# Navigate to Docker directory
cd docker/postgresql

# Start PostgreSQL container (Docker Desktop must be running)
docker-compose up -d

# Verify container is running
docker ps
# Expected: Container "finwise-postgresql" status "Up"

# Test database connection
docker exec -it finwise-postgresql psql -U finwise -d finwise_db -c "SELECT version();"
# Expected: PostgreSQL 18.x version output
```

**Database Configuration**:
- **Host**: `localhost`
- **Port**: `5432`
- **Database**: `finwise_db`
- **Username**: `finwise`
- **Password**: `finwise_dev_password` (stored in `docker-compose.yml`)

### Step 3: Configure Azure OpenAI Service

Create environment variables for Azure OpenAI API credentials:

```powershell
# Set environment variables (persist for current session)
$env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY = "your-api-key-here"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o-mini"

# Verify variables set correctly
echo $env:AZURE_OPENAI_ENDPOINT
echo $env:AZURE_OPENAI_API_KEY
```

**To persist environment variables across sessions** (optional):
```powershell
[System.Environment]::SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://your-resource.openai.azure.com/", "User")
[System.Environment]::SetEnvironmentVariable("AZURE_OPENAI_API_KEY", "your-api-key-here", "User")
[System.Environment]::SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME", "gpt-4o-mini", "User")
```

### Step 4: Run Database Migrations (Implementation Phase 2 - Persistent Profiles)

> **Skip this step if doing Implementation Phase 1 POC only** (no database). Implementation Phase 2 with persistent profiles is the recommended setup path.

```powershell
# Navigate to User Profile MCP Server project
cd src/FinWise.UserProfile.McpServer

# Apply EF Core migrations to PostgreSQL
dotnet ef database update

# Expected output: "Done." (migrations applied successfully)

# Verify schema created
docker exec -it finwise-postgresql psql -U finwise -d finwise_db -c "\dt"
# Expected: Table "user_profiles" listed
```

### Step 5: Build Projects

```powershell
# Return to repository root
cd ../..

# Build Orchestrator MCP server (always required)
dotnet build src/FinWise.Orchestrator/FinWise.Orchestrator.csproj

# Build User Profile MCP Server (Implementation Phase 2 only - skip for Implementation Phase 1 POC)
dotnet build src/FinWise.UserProfile.McpServer/FinWise.UserProfile.McpServer.csproj

# Expected: "Build succeeded. 0 Warning(s). 0 Error(s)."
```

> **Implementation Phase 1 POC users**: Only build FinWise.Orchestrator - the User Profile MCP Server is not needed for in-memory profiles.

---

## Configuration: MCP Client Setup

### GitHub Copilot (VS Code)

1. **Ensure environment variables are set**:
   
   The MCP server requires Azure OpenAI credentials to be available in your system environment. Verify they are set:
   
   ```powershell
   # Verify environment variables
   echo $env:AZURE_OPENAI_ENDPOINT
   echo $env:AZURE_OPENAI_API_KEY
   echo $env:AZURE_OPENAI_DEPLOYMENT_NAME
   ```
   
   If not set, configure them as shown in Step 3 above.

2. **Open VS Code settings**:
   - Press `Ctrl+,` or go to File → Preferences → Settings
   - Click the "Open Settings (JSON)" icon in the top right

3. **Add MCP server configuration to `settings.json`**:

```json
{
  "mcp.servers": {
    "FinWise-Orchestrator-MCP": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "src/finwise-orchestrator-mcp-server/finwise-orchestrator-mcp-server.csproj"
      ],
      "env": {
        "AZURE_OPENAI_ENDPOINT": "${env:AZURE_OPENAI_ENDPOINT}",
        "AZURE_OPENAI_API_KEY": "${env:AZURE_OPENAI_API_KEY}",
        "AZURE_OPENAI_DEPLOYMENT_NAME": "${env:AZURE_OPENAI_DEPLOYMENT_NAME}"
      }
    }
  }
}
```

**Important**: 
- The `${env:VARIABLE_NAME}` syntax references your system environment variables
- Ensure the path to `finwise-orchestrator-mcp-server.csproj` is correct relative to your workspace root
- If using workspace settings, path should be relative; for user settings, use absolute path

4. **Restart VS Code**

5. **Verify MCP server connection**:
   - Open GitHub Copilot Chat
   - Type `@workspace` and look for available MCP tools
   - Should show "FinWise-Orchestrator-MCP" with 3 tools:
     - `get_investment_recommendations`
     - `update_user_profile`
     - `get_conversation_history`

---

## Usage Examples

### Example 1: First-Time User - Complete Workflow

**User**: "I need investment advice"

**Expected Behavior**:
1. Orchestrator receives query, detects no existing user profile
2. Routes to User Profile Agent
3. User Profile Agent asks questionnaire questions (multi-turn conversation - 1 question per turn):
   - Turn 1: "What is your risk tolerance? (Conservative, Moderate, or Aggressive)"
   - Turn 2: "What are your investment goals? (Retirement, Home Purchase, Wealth Building, Education)"
   - Turn 3: "What is your investment timeframe? (Short-term: 1-3 years, Medium-term: 3-10 years, Long-term: 10+ years)"
   - **Pattern**: Agent waits for response after each question before asking next
4. User Profile Agent saves profile to PostgreSQL via User Profile MCP Server
5. Handoff to Global Advisor Agent with user profile context
6. Global Advisor Agent generates generic investment advice:
   - Example: "Based on your moderate risk tolerance and long-term retirement goals, a balanced portfolio of 60% stocks and 40% real estate could be suitable..."

**Conversation Flow**:
```
User: I need investment advice
FinWise (Orchestrator): Routing to User Profile Agent to collect your investment preferences...

FinWise (UserProfile): Welcome! Let me gather some information about your investment profile.
What is your risk tolerance?
  - Conservative (prefer stable, lower-risk investments)
  - Moderate (balanced risk and return)
  - Aggressive (higher risk for potential higher returns)

User: Moderate

FinWise (UserProfile): What are your investment goals? (Select all that apply)
  - Retirement
  - Home Purchase
  - Wealth Building
  - Education

User: Retirement and Wealth Building

FinWise (UserProfile): What is your investment timeframe?
  - Short-term (1-3 years)
  - Medium-term (3-10 years)
  - Long-term (10+ years)

User: Long-term

FinWise (UserProfile): Great! I've saved your profile. Handing off to our Global Investment Advisor...

FinWise (GlobalAdvisor): Based on your profile (Moderate risk, Retirement + Wealth Building goals, Long-term horizon), I recommend:
  - 60% allocation to diversified stock index funds (S&P 500, international stocks)
  - 30% allocation to real estate (REITs or direct property ownership)
  - 10% allocation to bonds for stability
  
This balanced approach aligns with your moderate risk tolerance and long-term goals.
```

### Example 2: Returning User - Direct Investment Query

**User**: "Should I invest in stocks or real estate?" *(Assumes user profile already exists)*

**Expected Behavior**:
1. Orchestrator detects investment query
2. Retrieves user profile from database (via User Profile MCP Server)
3. Routes directly to Global Advisor Agent with profile context
4. Global Advisor generates advice based on existing profile

**Conversation**:
```
User: Should I invest in stocks or real estate?

FinWise (GlobalAdvisor): Based on your profile (Moderate risk, Retirement goals, Long-term horizon):
  
  Stocks offer higher growth potential over long-term horizons (10+ years), making them well-suited for retirement savings. With moderate risk tolerance, diversified stock index funds provide balanced exposure.
  
  Real estate provides stable income through rental yields and hedge against inflation, complementing stocks in a balanced portfolio.
  
  Recommendation: 70% stocks (diversified index funds) + 30% real estate (REITs or direct ownership).
```

### Example 3: Back-Navigation - Update Profile Mid-Conversation

**User**: "Actually, I want to change my risk tolerance to Aggressive"

**Expected Behavior**:
1. Orchestrator detects profile update request
2. Routes to User Profile Agent (back-navigation)
3. User Profile Agent updates profile in PostgreSQL
4. Routes back to Global Advisor Agent with updated profile
5. Global Advisor revises recommendations

**Conversation**:
```
User: Actually, I want to change my risk tolerance to Aggressive

FinWise (Orchestrator): Routing to User Profile Agent to update your profile...

FinWise (UserProfile): I've updated your risk tolerance to Aggressive. Would you like to review your investment goals and timeframe as well, or keep them the same?

User: Keep them the same

FinWise (UserProfile): Profile updated successfully. Handing back to Global Advisor with updated profile...

FinWise (GlobalAdvisor): With your updated profile (Aggressive risk, Retirement + Wealth Building, Long-term):
  
  Recommendation: 85% stocks (higher growth potential with aggressive stance) + 15% real estate for diversification.
  
  Consider:
  - Growth stocks and emerging markets for higher returns
  - Real estate in high-growth markets
  - Accept higher volatility for long-term gains
```

### Example 4: Agent Escalation - Query Beyond Capability

**User**: "Should I buy MSFT stock right now?"

**Expected Behavior**:
1. Orchestrator routes to Global Advisor Agent
2. Global Advisor detects specific stock query (beyond generic stock vs. real estate guidance)
3. Global Advisor escalates to Orchestrator
4. Orchestrator returns fallback message (Stock Fundamentals Agent not available in v0.1)

**Conversation**:
```
User: Should I buy MSFT stock right now?

FinWise (GlobalAdvisor): I provide general stock vs. real estate guidance, but specific stock analysis (like MSFT fundamentals, P/E ratios, earnings) requires our Stock Fundamentals Agent.

FinWise (Orchestrator): Our Stock Fundamentals Agent will be available in v0.2 (coming soon). For now, I can provide generic advice: stocks are suitable for long-term growth with moderate to aggressive risk tolerance.

For specific stock picks, please consult:
  - Financial advisors
  - Stock research platforms (Yahoo Finance, Morningstar)
  - Company financial statements and earnings reports
```

---

## Testing

### Manual Testing Checklist

Run through these scenarios to verify v0.1 functionality:

**Scenario 1: First-Time User Workflow**
- [ ] User query "I need investment advice" routes to User Profile Agent
- [ ] User Profile Agent asks 3 questionnaire questions (multi-turn: 1 question per turn - risk tolerance → goals → timeframe)
- [ ] Agent waits for your response after each question before asking the next
- [ ] Profile saved to PostgreSQL (check database: `SELECT * FROM user_profiles;`)
- [ ] Handoff to Global Advisor Agent with profile context
- [ ] Global Advisor returns generic stock vs. real estate advice
- [ ] Full workflow completes in <10 seconds

**Scenario 2: Returning User - Direct Query**
- [ ] User query "Should I invest in stocks?" routes directly to Global Advisor
- [ ] Global Advisor retrieves existing profile from database
- [ ] Recommendation aligns with user's risk tolerance and goals

**Scenario 3: Back-Navigation - Profile Update**
- [ ] User requests profile change mid-conversation
- [ ] Orchestrator routes back to User Profile Agent
- [ ] Profile updated in database (check `updated_at` timestamp)
- [ ] Global Advisor receives updated profile and revises advice

**Scenario 4: Agent Escalation - Specific Stock Query**
- [ ] User asks about specific stock (e.g., "Should I buy MSFT?")
- [ ] Global Advisor escalates to Orchestrator
- [ ] Orchestrator returns fallback message (Stock Fundamentals Agent not available in v0.1)

**Scenario 5: Conversation History**
- [ ] User requests conversation history via `get_conversation_history` MCP tool
- [ ] Returns last 10 messages with agent transitions and handoffs

### Automated Testing

Run unit and integration tests:

```powershell
# Run all tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverageReportFormat=opencover

# Expected: All tests pass, ≥80% coverage
```

### Database Inspection

Verify user profiles stored correctly:

```powershell
# Connect to PostgreSQL
docker exec -it finwise-postgresql psql -U finwise -d finwise_db

# Query user profiles
SELECT user_identifier, risk_tolerance, investment_goals, investment_timeframe, created_at
FROM user_profiles
ORDER BY created_at DESC;

# Expected: User profiles from testing scenarios
```

---

## Troubleshooting

### Issue: MCP Server Not Appearing in VS Code

**Symptoms**: MCP tools not available in GitHub Copilot Chat, or "FinWise-Orchestrator-MCP" not listed

**Solutions**:
1. Verify environment variables are set: `echo $env:AZURE_OPENAI_ENDPOINT`
2. Check `settings.json` syntax is valid (JSON linter in VS Code will show errors)
3. Verify path to `finwise-orchestrator-mcp-server.csproj` is correct relative to workspace root
4. Check VS Code Output panel (View → Output) and select "MCP" from dropdown
5. Restart VS Code completely (close all windows)
6. Ensure GitHub Copilot extension is updated to latest version with MCP support

### Issue: Database Connection Failed

**Symptoms**: Error "Unable to connect to PostgreSQL database"

**Solutions**:
1. Verify PostgreSQL container running: `docker ps`
2. If stopped, start container: `docker-compose up -d`
3. Check connection string in `appsettings.json` matches Docker config
4. Test connection: `docker exec -it finwise-postgresql psql -U finwise -d finwise_db`
5. Verify port 5432 not blocked by firewall

### Issue: Azure OpenAI API Errors

**Symptoms**: Error "Failed to generate investment advice" or "LLM service error"

**Solutions**:
1. Verify environment variables set: `echo $env:AZURE_OPENAI_ENDPOINT`
2. Check API key valid in Azure Portal → OpenAI resource → Keys
3. Verify `gpt-4o-mini` deployment exists in Azure OpenAI resource
4. Check API quota not exceeded (free tier: 120K tokens/month)
5. Test API directly: Use Azure OpenAI Studio Playground

### Issue: Workflow Times Out (>10 seconds)

**Symptoms**: "Investment recommendation request timed out after 10 seconds"

**Solutions**:
1. Check Azure OpenAI latency (use `gpt-4o-mini` not `gpt-4` for faster responses)
2. Verify network connection stable
3. Framework automatically manages conversation context during workflow execution
4. Check Docker Desktop CPU/memory allocation (recommend 4GB+ RAM)

### Issue: Circular Escalation Detected

**Symptoms**: Error "Circular agent escalation detected: A → B → A"

**Expected Behavior**: This is intentional - system prevents infinite loops

**Solutions**:
1. Review conversation history to understand escalation pattern
2. If legitimate use case, redesign agent capabilities (escalation indicates missing agent)
3. Check AgentWorkflowBuilder handoff configuration - framework prevents invalid circular handoffs at build time

---

## Performance Benchmarks

Expected performance targets for v0.1:

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| **Full workflow latency** | <10 seconds | User query → final advisor response |
| **MCP tool invocation** | <500ms | Database read/write via User Profile MCP Server |
| **Agent handoff** | <1 second | Context transfer between agents |
| **Database query** | <100ms | PostgreSQL SELECT on indexed `user_identifier` |
| **LLM inference** | <2 seconds | Azure OpenAI `gpt-4o-mini` response (p95) |

**To measure performance**:
```powershell
# Enable detailed logging (appsettings.json)
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.Agents": "Debug"
  }
}

# Check logs for execution times
# Example log entry:
# [14:35:12 INF] Agent handoff from UserProfile to GlobalAdvisor completed in 245ms
```

---

## Next Steps

After completing the quickstart:

1. **Explore Advanced Scenarios**:
   - Multi-turn conversations with 5+ agent transitions
   - Profile updates during different workflow stages
   - Testing all MCP tools (`get_investment_recommendations`, `update_user_profile`, `get_conversation_history`)

2. **Customize Agents** (for developers):
   - Modify `GlobalAdvisorAgent.cs` to provide more detailed advice
   - Extend questionnaire in `UserProfileAgent.cs`
   - Add custom logging or telemetry

3. **Prepare for v0.2** (Docker Compose deployment):
   - Review v0.2 architecture in `specs/03-architecture-and-technologies.md`
   - Plan for additional agents (Stock Fundamentals, Real Estate)
   - Consider persistent conversation history (Cosmos DB)

---

## Support and Resources

- **Documentation**: `/docs/` directory in repository
- **Architecture**: `specs/03-architecture-and-technologies.md`
- **Feature Spec**: `specs/001-core-workflow/spec.md`
- **API Contracts**: `specs/001-core-workflow/contracts/`
- **Issues**: GitHub Issues (tag: `v0.1`)

---

## Appendix: Environment Variables Reference

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `AZURE_OPENAI_ENDPOINT` | Yes | Azure OpenAI resource endpoint | `https://my-resource.openai.azure.com/` |
| `AZURE_OPENAI_API_KEY` | Yes | Azure OpenAI API key | `abc123...xyz789` |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Yes | Deployed model name | `gpt-4o-mini` |
| `DATABASE_CONNECTION_STRING` | No* | PostgreSQL connection string | `Host=localhost;Port=5432;Database=finwise_db;Username=finwise;Password=finwise_dev_password` |
| `LOG_LEVEL` | No | Logging verbosity | `Information`, `Debug`, `Warning` |
| `SESSION_TIMEOUT_MINUTES` | No | Conversation session expiry | `30` (default) |

*Default connection string used from `appsettings.json` if not provided.
