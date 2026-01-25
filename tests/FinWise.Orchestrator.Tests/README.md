# FinWise Orchestrator End-to-End Functional Tests

This project contains comprehensive end-to-end functional tests for the FinWise financial advisor MCP server, validating complete user journeys via HTTP MCP protocol.

---

## 📋 Test Overview

### Test Suite: `EndToEndMcpTests.cs`

| Test | Purpose | What It Validates |
|------|---------|-------------------|
| **CompleteUserJourney_NewUser** | New user creates profile and gets advice | Profile collection, saving, automatic answer |
| **ProfileRetrieval_SameSession** | Profile retrieval in same session | Email retention, profile display |
| **EmailRetention_SameSession** | Email not re-requested in same session | Conversation history search |
| **ReturningUser_NewSession** | Existing profile loaded in new session | Cross-session profile persistence |
| **TwoSessions_SameEmail** | Profile reuse across two different questions | Complete lifecycle: create → persist → reuse |

---

## 🚀 Running Tests in VS Code

### Prerequisites

1. **Install .NET 10 SDK**
   - Download from: https://dotnet.microsoft.com/download
   - Verify installation: `dotnet --version` (should show 10.x.x)

2. **Install VS Code Extensions**
   - **C# Dev Kit** (microsoft.csdevkit) - Required for .NET testing
   - **C#** (ms-dotnettools.csharp) - C# language support
   - **.NET Core Test Explorer** (optional, provides visual test runner)

3. **Azure OpenAI Configuration**
   - Set environment variables OR configure `appsettings.json`
   - Required variables:
     ```powershell
     $env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
     $env:AZURE_OPENAI_API_KEY = "your-api-key"
     $env:AZURE_OPENAI_DEPLOYMENT = "gpt-4o-mini"
     ```

---

### Method 1: Run Tests via VS Code Test Explorer (Recommended)

#### Step 1: Start FinWise Server

Open a new terminal in VS Code (**Terminal → New Terminal**) and run:

```powershell
# Navigate to orchestrator project
cd src/FinWise.Orchestrator

# Start the server on port 3923
dotnet run --project FinWise.Orchestrator.csproj --urls http://127.0.0.1:3923
```

**Keep this terminal running!** The server must be active for tests to work.

#### Step 2: Discover Tests

1. Click the **Testing** icon in VS Code sidebar (beaker icon)
2. Tests should automatically appear under "FinWise.Orchestrator.Tests"
3. If not visible, click the **Refresh** button in the Test Explorer

#### Step 3: Run Tests

**Run All Tests:**
- Click the **Play All** button at the top of Test Explorer

**Run Individual Test:**
- Hover over any test name
- Click the **Play** button next to the test

**Debug Test:**
- Right-click on a test
- Select **Debug Test**
- Set breakpoints in test code as needed

#### Step 4: View Test Output

- Test results appear in the Test Explorer
- ✅ Green checkmark = Passed
- ❌ Red X = Failed
- Click on any test to see detailed output in the terminal

---

### Method 2: Run Tests via Command Line

#### Option A: Run All Tests

```powershell
# From repository root
dotnet test tests/FinWise.Orchestrator.Tests/
```

#### Option B: Run Specific Test

```powershell
# Run only the double session test
dotnet test tests/FinWise.Orchestrator.Tests/ --filter "FullyQualifiedName~TwoSessions_SameEmail"

# Run only new user journey test
dotnet test tests/FinWise.Orchestrator.Tests/ --filter "FullyQualifiedName~CompleteUserJourney_NewUser"

# Run only profile retrieval test
dotnet test tests/FinWise.Orchestrator.Tests/ --filter "FullyQualifiedName~ProfileRetrieval_SameSession"
```

#### Option C: Run with Verbose Output

```powershell
# See detailed test execution logs
dotnet test tests/FinWise.Orchestrator.Tests/ --logger "console;verbosity=detailed"
```

---

### Method 3: Run Tests with Live Unit Testing (Real-time)

1. Open any test file in VS Code
2. Install **C# Dev Kit** extension (if not already installed)
3. Look for **Run Test** / **Debug Test** CodeLens links above each test method
4. Click **Run Test** to execute individual tests
5. Results appear inline in the editor

---

## 📊 Understanding Test Results

### Expected Test Duration
- Single test: ~5-15 seconds (depends on LLM response time)
- Full suite: ~30-60 seconds (5 tests)

### Successful Test Output Example

```
✅ CompleteUserJourney_NewUser_ShouldCreateProfileAndProvideAdvice
   Duration: 12.3s
   
   Output:
   Test session ID: abc123xyz456
   === STEP 1: Request financial advice ===
   Response 1: Please provide your email address.
   === STEP 2: Provide email ===
   Response 2: What is your risk tolerance: Conservative, Moderate, or Aggressive?
   ...
   === STEP 5: Provide timeframe ===
   PROFILE_READY: email=adrian... risk=Moderate goals=Increase profit timeframe=Long-term
   [Financial advice provided based on profile]
```

### Common Failure Reasons

1. **Server Not Running**
   ```
   Error: HttpRequestException: Connection refused
   ```
   **Fix**: Start FinWise server on http://127.0.0.1:3923

2. **Azure OpenAI Not Configured**
   ```
   Error: Azure.RequestFailedException: Unauthorized
   ```
   **Fix**: Set Azure OpenAI environment variables

3. **Timeout**
   ```
   Error: TaskCanceledException: The operation was canceled
   ```
   **Fix**: LLM taking too long; retry or increase timeout

4. **Assertion Failed**
   ```
   Assert.Contains() Failure
   Expected: "PROFILE_READY:"
   Actual: "..."
   ```
   **Fix**: Check test logs to see actual system response

---

## 🔍 Debugging Tests

### Attach Debugger to Tests

1. **Set Breakpoint** in test code
2. Right-click on test in Test Explorer
3. Select **Debug Test**
4. Debugger stops at breakpoint
5. Inspect variables, step through code

### View Detailed Logs

Tests use `ITestOutputHelper` to output detailed logs:

```csharp
_output.WriteLine("=== STEP 1: Request financial advice ===");
_output.WriteLine($"Response: {response}");
```

**View Logs**:
- VS Code Test Explorer: Click on test → View output tab
- Command line: Run with `--logger "console;verbosity=detailed"`

### Troubleshooting Tips

**Problem**: Test hangs/freezes
- **Cause**: Waiting for server response that never comes
- **Fix**: Check server logs, verify endpoint is http://127.0.0.1:3923/mcp

**Problem**: Profile not persisting between tests
- **Cause**: In-memory store cleared between test runs
- **Fix**: Tests are designed to run independently; each test sets up its own data

**Problem**: Flaky test failures
- **Cause**: LLM non-deterministic responses
- **Fix**: Increase delays between steps, broaden assertion text matching

---

## 🧪 Test Architecture

### Test Flow

```
Test → HttpClient → MCP Server (http://127.0.0.1:3923/mcp)
                         ↓
                  UserProfileAgent
                         ↓
                  FinancialAdvisorAgent
                         ↓
                  Azure OpenAI GPT-4o-mini
```

### Key Components

- **MCP Protocol**: JSON-RPC 2.0 over HTTP
- **Session Management**: Via `MCP-Session-Id` header
- **Profile Store**: In-memory (per server process)
- **Conversation Store**: In-memory (per session)

### Test Helpers

- `CallFinancialAdviceTool(query, sessionId)` - Sends MCP tool call
- `SetupTestProfile()` - Creates test profile with delays
- `XunitLogger` - Outputs logs to xUnit test console

---

## 📝 Adding New Tests

### Template

```csharp
[Fact]
public async Task YourTest_Scenario_ExpectedBehavior()
{
    // Arrange
    string sessionId = Guid.NewGuid().ToString("N")[..16];
    string testEmail = "test@example.com";
    
    // Act
    var response = await CallFinancialAdviceTool("Your question", sessionId);
    
    // Assert
    Assert.Contains("expected text", response.ToLowerInvariant());
    _output.WriteLine($"Response: {response}");
}
```

### Best Practices

1. **Use unique session IDs** per test to avoid interference
2. **Add delays** after profile operations (200-500ms)
3. **Use lowercase assertions** for case-insensitive matching
4. **Log all responses** via `_output.WriteLine()` for debugging
5. **Clean up** in `Dispose()` if needed (HttpClient already disposed)

---

## 🎯 Test Coverage

### Current Coverage

✅ New user profile creation  
✅ Automatic original question answering  
✅ Profile retrieval in same session  
✅ Email retention within session  
✅ Profile loading in new session  
✅ Cross-session profile reuse with different questions  

### Future Test Ideas

- ❌ Multiple users in parallel sessions
- ❌ Profile update scenarios
- ❌ Invalid/malformed input handling
- ❌ Timeout and error recovery
- ❌ Concurrent access to same profile
- ❌ Profile deletion

---

## 🛠️ VS Code Tasks Configuration (Optional)

Create `.vscode/tasks.json` in repository root:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "Start FinWise Server",
            "type": "shell",
            "command": "dotnet",
            "args": [
                "run",
                "--project",
                "src/FinWise.Orchestrator/FinWise.Orchestrator.csproj",
                "--urls",
                "http://127.0.0.1:3923"
            ],
            "isBackground": true,
            "problemMatcher": {
                "pattern": {
                    "regexp": "^(.*)$",
                    "file": 1,
                    "location": 2,
                    "message": 3
                },
                "background": {
                    "activeOnStart": true,
                    "beginsPattern": "^.*Building.*",
                    "endsPattern": "^.*Application started.*"
                }
            },
            "presentation": {
                "reveal": "always",
                "panel": "dedicated"
            }
        },
        {
            "label": "Run All Tests",
            "type": "shell",
            "command": "dotnet",
            "args": [
                "test",
                "tests/FinWise.Orchestrator.Tests/"
            ],
            "dependsOn": "Start FinWise Server",
            "group": {
                "kind": "test",
                "isDefault": true
            }
        }
    ]
}
```

**Usage**: Press `Ctrl+Shift+P` → **Tasks: Run Task** → Select task

---

## 📚 Additional Resources

- **MCP Specification**: https://modelcontextprotocol.io/
- **xUnit Documentation**: https://xunit.net/
- **.NET Testing Guide**: https://learn.microsoft.com/en-us/dotnet/core/testing/
- **VS Code Testing**: https://code.visualstudio.com/docs/editor/testing

---

## 🤝 Contributing

When adding new tests:
1. Follow existing test naming conventions (`TestScenario_Condition_ExpectedBehavior`)
2. Add comprehensive assertions and logging
3. Update this README with test descriptions
4. Ensure tests are independent and repeatable

---

## ℹ️ Support

**Issues with tests?**
- Check server logs: `finwise-[date].log`
- Review test output in VS Code Test Explorer
- Verify Azure OpenAI configuration
- Ensure server is running on correct port

**Need help?**
- Review [TEST-REVIEW-MCP-E2E.md](../../docs/TEST-REVIEW-MCP-E2E.md) for detailed test explanations
- Check [BUG-FIX-NEW-SESSION-EMAIL-REQUIREMENT.md](../../docs/BUG-FIX-NEW-SESSION-EMAIL-REQUIREMENT.md) for architecture context
