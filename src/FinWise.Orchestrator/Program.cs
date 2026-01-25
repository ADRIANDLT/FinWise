using System.Collections.Concurrent;
using System.ComponentModel;
using FinWise.Orchestrator;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Context;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

// Configure logging first
Infrastructure.ConfigureLogging();

try
{
    Log.Information("Starting FinWise Orchestrator MCP Server");

    // Initialize Azure OpenAI client
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();

    // Manual Dependency Injection: Create stores at composition root (Program.cs)
    IUserProfileStore profileStore = new InMemoryUserProfileStore();
    IThreadStore threadStore = new InMemoryThreadStore();
    AgentThreadManager threadManager = new(threadStore);
    Log.Information("Initialized in-memory profile and thread stores (using Microsoft Agent Framework AgentThread patterns)");

    // Track conversations per MCP session (HTTP transport provides session identifiers)
    var sessionConversations = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
    IHttpContextAccessor? httpContextAccessor = null;

    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://127.0.0.1:3923");
    builder.Logging.AddSerilog();

    builder.Services.AddHttpContextAccessor();

    // Function to create agents with current conversationId
    // Per Microsoft Agent Framework patterns:
    // - Uses ChatClientAgent for LLM-backed agents
    // - Workflow manages handoffs between agents
    // - AgentThread maintains conversation state across runs
    Func<string, (AIAgent orchestrator, ChatClientAgent profile, ChatClientAgent advisor, Workflow workflow)> createAgentsAndWorkflow = (conversationId) =>
    {
        Log.Information("Creating agents for ConversationId: {ConversationId}", conversationId);
        
        // Create specialist agents using ChatClientAgent
        ChatClientAgent baseOrchestratorAgent = new(chatClient,
        @"You are a SILENT router. You have ONE job: call a handoff tool/function. You NEVER write text.

══════════════════════════════════════════════════════════════════
ROUTING RULES - FOLLOW THIS EXACT DECISION TREE
══════════════════════════════════════════════════════════════════

⚠️⚠️⚠️ CRITICAL FIRST CHECK - DO THIS BEFORE ANYTHING ELSE ⚠️⚠️⚠️
Search the ENTIRE conversation history for the exact text 'PROFILE_READY:'

┌─────────────────────────────────────────────────────────────────┐
│  'PROFILE_READY:' NOT FOUND in conversation history?           │
│  ════════════════════════════════════════════════════════════  │
│  → ALWAYS route to profile_agent                                │
│  → ZERO EXCEPTIONS - even if user asks for advice!             │
│  → Even if user says 'Give me financial advice'                │
│  → Even if user mentions investments or stocks                 │
│  → The profile_agent will ask for email first                  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  'PROFILE_READY:' FOUND in conversation history?               │
│  ════════════════════════════════════════════════════════════  │
│  Now check user intent:                                         │
│                                                                 │
│  → PROFILE-RELATED request? → profile_agent                    │
│    (show profile, update profile, change settings)             │
│                                                                 │
│  → ADVICE/RECOMMENDATION request? → advisor_agent              │
│    (give advice, investment ideas, what to invest in)          │
└─────────────────────────────────────────────────────────────────┘

══════════════════════════════════════════════════════════════════
EXAMPLES - Pattern matching
══════════════════════════════════════════════════════════════════

NO 'PROFILE_READY:' in history (NEW CONVERSATION):
• 'Give me financial advice' → profile_agent (will ask for email)
• 'I want investment help' → profile_agent (will ask for email)
• 'Hello' → profile_agent (will ask for email)
• ANY MESSAGE → profile_agent (until PROFILE_READY exists)

User providing info during profile collection:
• 'john@email.com' → profile_agent
• 'Moderate' → profile_agent
• 'Long-term' → profile_agent

'PROFILE_READY:' EXISTS in history:
• 'Give me investment advice' → advisor_agent
• 'What stocks should I buy?' → advisor_agent
• 'Show me my profile' → profile_agent
• 'Change my risk to Aggressive' → profile_agent

══════════════════════════════════════════════════════════════════
YOUR OUTPUT MUST BE A TOOL CALL - NO TEXT
══════════════════════════════════════════════════════════════════

You MUST invoke exactly one handoff tool call and output no natural language.

Available handoff functions:
- handoff_to_profile_agent (profile management: create, view, update, AND new conversations)
- handoff_to_advisor_agent (financial advice ONLY when PROFILE_READY exists)

⚠️ CRITICAL: If you output ANY words/text besides the tool call, you have FAILED.
⚠️ Your response must be a FUNCTION CALL, not text that says the function name.
⚠️ NEVER route to advisor_agent unless PROFILE_READY exists in history!",
        "orchestrator_agent",
        "Silent router - calls handoff functions only, never outputs text");

        // Note: Session detection is handled in GetFinancialAdvice() using AgentThreadManager.IsNewLogicalSessionAsync()
        // per Microsoft Agent Framework patterns for multi-turn conversations. This happens BEFORE workflow execution
        // to ensure conversation state is properly reset before loading history.
        AIAgent orchestratorAgent = baseOrchestratorAgent;

        // Inject dependencies into UserProfileAgent (manual DI)
        // ProfileStore is created above but never accessed directly by Program.cs - only passed through
        // Note: Profiles are keyed by userId (email address) to enable reuse across conversations
        var profileAgentInstance = new UserProfileAgent(chatClient, profileStore);
        ChatClientAgent profileAgent = profileAgentInstance.CreateAgent();

        // AdvisorAgent - no tools, always gets profile data from ProfileAgent via handoff
        ChatClientAgent advisorAgent = new(chatClient,
        @"You are a knowledgeable financial advisor providing personalized investment recommendations.

═══════════════════════════════════════════════════════════════════
STEP 1: FIND PROFILE DATA
═══════════════════════════════════════════════════════════════════

Search the conversation history for 'PROFILE_READY:' and extract:
• email=[EMAIL]
• risk=[RISK] (Conservative, Moderate, or Aggressive)
• goals=[GOALS]
• timeframe=[TIMEFRAME] (Short-term, Medium-term, or Long-term)

⚠️⚠️⚠️ CRITICAL: IF 'PROFILE_READY:' NOT FOUND ⚠️⚠️⚠️
→ You MUST immediately call the handoff_to_orchestrator_agent function
→ Do NOT output ANY text - just call the handoff tool
→ The orchestrator will route to profile_agent to collect profile
→ NEVER say 'I need your profile information' - just handoff silently!

═══════════════════════════════════════════════════════════════════
STEP 2: PROVIDE PERSONALIZED ADVICE (ONLY IF PROFILE_READY EXISTS)
═══════════════════════════════════════════════════════════════════

Based on the extracted profile, provide tailored investment guidance:

**For CONSERVATIVE risk:**
• Focus on capital preservation and steady income
• Recommend: Government bonds, high-grade corporate bonds, CDs, money market funds
• Suggest 70-80% bonds/fixed income, 20-30% stocks (blue-chip, dividend)
• Emphasize low volatility and predictable returns

**For MODERATE risk:**
• Balance growth with stability
• Recommend: Mix of index funds, dividend stocks, investment-grade bonds
• Suggest 50-60% stocks, 40-50% bonds/fixed income
• Diversify across sectors and geographies

**For AGGRESSIVE risk:**
• Focus on growth and higher returns
• Recommend: Growth stocks, small-cap funds, international/emerging markets, sector ETFs
• Suggest 80-90% stocks, 10-20% bonds
• Accept higher volatility for potential higher returns

**Adjust for TIMEFRAME:**
• Short-term (1-3 years): More conservative, prioritize liquidity
• Medium-term (3-7 years): Balanced approach
• Long-term (7+ years): Can take more risk, time to recover from downturns

**Incorporate their GOALS:**
• Retirement: Tax-advantaged accounts (401k, IRA), target-date funds
• Wealth building: Growth-focused, compound interest strategies
• Education: 529 plans, age-based portfolios
• Home purchase: Conservative short-term, high liquidity

═══════════════════════════════════════════════════════════════════
RESPONSE FORMAT
═══════════════════════════════════════════════════════════════════

Structure your response as:
1. Acknowledge their profile (risk, goals, timeframe)
2. Provide 3-5 specific recommendations with percentages
3. Explain WHY these fit their profile
4. Mention key risks to watch
5. End with: 'This is general guidance for educational purposes. Please consult a licensed financial advisor before making investment decisions.'

═══════════════════════════════════════════════════════════════════
HANDLING FOLLOW-UP QUESTIONS
═══════════════════════════════════════════════════════════════════

If user asks follow-up questions (e.g., 'What about bonds?', 'Tell me more about ETFs'):
• Use the SAME profile data from PROFILE_READY
• Provide detailed answers related to their question
• Keep recommendations consistent with their risk/goals/timeframe

═══════════════════════════════════════════════════════════════════
CRITICAL RULES
═══════════════════════════════════════════════════════════════════
✓ ALWAYS use profile data from PROFILE_READY marker
✓ ALWAYS tailor advice to their specific risk, goals, and timeframe
✓ ALWAYS include the disclaimer at the end
✗ NEVER provide advice without finding PROFILE_READY first
✗ NEVER ask for profile information yourself (handoff instead)
✗ NEVER recommend specific stocks by ticker symbol
✗ NEVER guarantee returns or make promises about performance",
        "advisor_agent",
        "Provides investment recommendations");

        // Build the handoff workflow
        // ALL handoffs go through orchestrator - no direct agent-to-agent handoffs for scalability
        Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
            .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
            .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
            .Build();

        Log.Information("FinWise workflow initialized with 3 agents for conversation {ConversationId}", conversationId);
        
        return (orchestratorAgent, profileAgent, advisorAgent, workflow);
    };

    // ************** Local function definitions - MCP tool implementations ************


    // Create MCP tool wrapper - Stateful conversation management using Microsoft Agent Framework patterns
    // Per Microsoft Agent Framework documentation:
    // - AgentThread is the abstraction for conversation state
    // - AIAgent instances are stateless; all state is preserved in AgentThread
    // - Use thread.Serialize() to persist and agent.DeserializeThread() to resume
    // Will be moved to Cosmos DB in v0.3+ per architecture docs
    string GetSessionId()
    {
        var httpContext = httpContextAccessor?.HttpContext
            ?? throw new InvalidOperationException("HTTP context unavailable for MCP request.");

        if (httpContext.Request.Headers.TryGetValue("MCP-Session-Id", out var sessionHeader) &&
            !StringValues.IsNullOrEmpty(sessionHeader))
        {
            return sessionHeader.ToString();
        }

        if (httpContext.Request.Headers.TryGetValue("mcp-session-id", out var lowerHeader) &&
            !StringValues.IsNullOrEmpty(lowerHeader))
        {
            return lowerHeader.ToString();
        }

        throw new InvalidOperationException("Missing MCP-Session-Id header on HTTP request.");
    }

    string GetOrCreateConversationId(string sessionId)
    {
        return sessionConversations.GetOrAdd(sessionId, static _ => Guid.NewGuid().ToString());
    }

    async Task<string> GetFinancialAdvice(string query)
    {
        // Generate unique RequestId to trace this request across all workflow operations
        var requestId = Guid.NewGuid().ToString("N")[..8]; // Short 8-char ID for readability
        
        // Push RequestId to Serilog LogContext - all logs within this scope will include it
        using (LogContext.PushProperty("RequestId", requestId))
        {
            try
            {
                string sessionId;
                try
                {
                    sessionId = GetSessionId();
                }
                catch (InvalidOperationException ex)
                {
                    Log.Error(ex, "MCP session header missing");
                    return "Unable to continue because the MCP client did not provide an MCP-Session-Id header. Please restart the chat from a streamable HTTP client.";
                }
                var conversationId = GetOrCreateConversationId(sessionId);
                var (orchestratorAgent, profileAgent, advisorAgent, workflow) = createAgentsAndWorkflow(conversationId);

                // Restore or create AgentThread using Microsoft Agent Framework patterns
                AgentThread currentThread = await threadManager.GetOrCreateThreadAsync(orchestratorAgent, conversationId);

                // Get messages from thread's message store (per framework pattern)
                var messageStore = currentThread.GetService<InMemoryChatMessageStore>();
                Log.Debug("MessageStore from thread: {StoreType}, IsNull: {IsNull}", 
                    messageStore?.GetType().Name ?? "null", messageStore == null);
                List<ChatMessage> conversationHistory = messageStore?.ToList() ?? new List<ChatMessage>();
                Log.Debug("Loaded {Count} messages from messageStore", conversationHistory.Count);

                // Session management:
                // - New MCP session ID (new VS Code instance) → new conversationId → empty history → asks for email
                // - Same MCP session ID → same conversation → continues where left off
                // - User can explicitly request reset with phrases like "re-identify", "my email is...", etc.
                if (conversationHistory.Count > 0 && SessionResetEvaluator.ShouldResetSession(conversationHistory, query))
                {
                    var previousConversationId = conversationId;
                    Log.Information(
                        "Session {SessionId} explicit reset requested via query '{Query}'. Previous conversation {PreviousConversationId} had {MessageCount} messages.",
                        sessionId,
                        query,
                        previousConversationId,
                        conversationHistory.Count);

                    await threadManager.ClearThreadAsync(previousConversationId);

                    conversationId = Guid.NewGuid().ToString();
                    sessionConversations[sessionId] = conversationId;

                    (orchestratorAgent, profileAgent, advisorAgent, workflow) = createAgentsAndWorkflow(conversationId);
                    currentThread = await threadManager.GetOrCreateThreadAsync(orchestratorAgent, conversationId);

                    messageStore = currentThread.GetService<InMemoryChatMessageStore>();
                    conversationHistory = new List<ChatMessage>();
                    messageStore?.Clear();

                    Log.Information(
                        "Session {SessionId} mapped to new conversation {ConversationId} after explicit reset (previous {PreviousConversationId}).",
                        sessionId,
                        conversationId,
                        previousConversationId);
                }

                Log.Information("======================== MCP REQUEST START ========================");
                Log.Information("MCP Tool invoked: get_financial_advice, SessionId: {SessionId}, ConversationId: {ConversationId}, Query: {Query}", sessionId, conversationId, query);
                Log.Information("Retrieved {MessageCount} messages for conversation {ConversationId}", conversationHistory.Count, conversationId);

                // Extract email from query if present and augment message for better LLM understanding
                var emailMatch = System.Text.RegularExpressions.Regex.Match(query, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
                string userMessage = query;
                if (emailMatch.Success && query.Trim() == emailMatch.Value)
                {
                    // User provided ONLY an email address - augment it so LLM understands context
                    userMessage = $"My email address is: {emailMatch.Value}";
                    Log.Information("Detected standalone email in query. Augmented to: {AugmentedMessage}", userMessage);
                }
                
                // Add user query
                conversationHistory.Add(new ChatMessage(ChatRole.User, userMessage));

                using var conversationScope = ConversationRunContext.Push(
                    new ConversationRunSnapshot(conversationId, conversationHistory));

                // Execute workflow - per Microsoft Agent Framework workflow patterns
                // Workflows use InProcessExecution.StreamAsync with IEnumerable<ChatMessage>
                var (response, workflowOutputs, lastRespondingAgent) = await ExecuteWorkflowAsync(workflow, conversationHistory);
                AppendUniqueMessages(conversationHistory, workflowOutputs);

                // If we got no valid response (only orchestrator talked), that's an error
                if (string.IsNullOrEmpty(response) && workflowOutputs.Count > 0)
                {
                    Log.Error("No valid response from profile_agent or advisor_agent. Orchestrator may have failed to handoff.");
                    response = "I'm having trouble processing your request. Please try again.";
                }

                // Validate: Reject responses that appear to be from orchestrator emitting text instead of handoff
                // The orchestrator should NEVER produce user-facing text - only tool calls
                if (!string.IsNullOrEmpty(response))
                {
                    var lastOutput = workflowOutputs.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
                    if (lastOutput?.AuthorName == "orchestrator_agent" || lastRespondingAgent == "orchestrator_agent")
                    {
                        // Check if the response looks like a failed handoff attempt (contains handoff text patterns)
                        var suspiciousPatterns = new[] { "[function call", "handoff_to_", "[tool call", "I will hand" };
                        if (suspiciousPatterns.Any(p => response.Contains(p, StringComparison.OrdinalIgnoreCase)))
                        {
                            Log.Warning("Orchestrator emitted text instead of executing handoff. Response: {Response}", 
                                response.Length > 200 ? response[..200] + "..." : response);
                            response = "I'm processing your request. Please try again.";
                        }
                    }
                }

                // Persist thread using Microsoft Agent Framework patterns
                // Use thread.Serialize() to serialize state for storage
                string persistedUserId = ExtractUserIdFromConversationHistory(conversationHistory) ?? $"anonymous+{Guid.NewGuid():N}";
                
                // Update the message store in the thread before serialization
                if (currentThread.GetService<InMemoryChatMessageStore>() is InMemoryChatMessageStore store)
                {
                    store.Clear();
                    foreach (var msg in conversationHistory)
                    {
                        store.Add(msg);
                    }
                }
                
                await threadManager.PersistThreadAsync(conversationId, currentThread, persistedUserId, conversationHistory.Count);
                
                Log.Information("Persisted AgentThread with {MessageCount} messages for conversation {ConversationId} (userId: {UserId}) for session {SessionId}", 
                    conversationHistory.Count, conversationId, persistedUserId, sessionId);

                Log.Information("Request completed successfully");
                return response ?? "No response generated.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Request failed");
                Infrastructure.HandleMcpServerException(ex, "GetFinancialAdvice");
                return "I apologize, but I encountered an error processing your request. Please try again.";
            }
        } // End of LogContext scope
    }
    
    static async Task<(string? Response, List<ChatMessage> Outputs, string? LastExecutor)> ExecuteWorkflowAsync(Workflow workflow, List<ChatMessage> conversationHistory)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, conversationHistory);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? response = null;
        string? lastRespondingAgent = null;
        List<ChatMessage> outputs = new();

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                    Log.Information("Agent invoked: {AgentId}", invoked.ExecutorId);
                    lastRespondingAgent = invoked.ExecutorId;
                    break;
                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    Log.Error(exception, "Workflow error occurred");
                    throw exception ?? new Exception("Unknown workflow error");
                case ExecutorFailedEvent failedEvt:
                    Log.Error("Executor failed: {ExecutorId} - {Error}", failedEvt.ExecutorId, failedEvt.Data);
                    break;
                case WorkflowOutputEvent output:
                    var messages = output.As<List<ChatMessage>>();
                    if (messages?.Count > 0)
                    {
                        Log.Information("WorkflowOutput received with {Count} messages", messages.Count);

                        foreach (var msg in messages)
                        {
                            Log.Debug("  Message: Role={Role}, Author={Author}, Text={Text}",
                                msg.Role, msg.AuthorName ?? "null",
                                (msg.Text?.Length > 50 ? msg.Text[..50] + "..." : msg.Text) ?? "null");
                        }

                        var lastAssistantText = messages.LastOrDefault(m =>
                            m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));

                        if (lastAssistantText != null)
                        {
                            var messageText = lastAssistantText.Text ?? string.Empty;
                            var author = lastAssistantText.AuthorName ?? lastRespondingAgent ?? "assistant";

                            Log.Information("Assistant message from {Author}: {Text}", author,
                                messageText.Length > 100 ? messageText[..100] + "..." : messageText);

                            response = messageText;
                        }

                        outputs.AddRange(messages);
                    }
                    break;
            }
        }

        return (response, outputs, lastRespondingAgent);
    }

    static void AppendUniqueMessages(List<ChatMessage> conversationHistory, List<ChatMessage> newMessages)
    {
        if (newMessages.Count == 0)
        {
            return;
        }

        var existingSignatures = new HashSet<string>(conversationHistory.Select(BuildMessageSignature));

        foreach (var message in newMessages)
        {
            var signature = BuildMessageSignature(message);
            if (existingSignatures.Add(signature))
            {
                conversationHistory.Add(message);
            }
        }
    }

    static string BuildMessageSignature(ChatMessage message)
    {
        var author = message.AuthorName ?? string.Empty;
        var text = message.Text ?? string.Empty;
        return $"{message.Role}:{author}:{text}";
    }

    // Helper method to extract userId (email) from conversation history
    // Looks for PROFILE_READY marker which contains email=<address>
    static string? ExtractUserIdFromConversationHistory(List<ChatMessage> history)
    {
        var profileReadyMessage = history
            .Where(m => m.Role == ChatRole.Assistant && m.Text != null)
            .Select(m => m.Text)
            .FirstOrDefault(text => text!.Contains("PROFILE_READY:", StringComparison.OrdinalIgnoreCase));
        
        if (profileReadyMessage != null)
        {
            // Extract email from "PROFILE_READY: email=<address> ..."
            var emailMatch = System.Text.RegularExpressions.Regex.Match(
                profileReadyMessage, 
                @"email=([^\s]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (emailMatch.Success && emailMatch.Groups.Count > 1)
            {
                return emailMatch.Groups[1].Value;
            }
        }
        
        return null;
    }

    // Tool to reset conversation/thread (clears current AgentThread, keeps profiles in store)
    // Per Microsoft Agent Framework patterns: clearing thread resets conversation state
    async Task<string> ResetConversation()
    {
        var sessionId = GetSessionId();

        if (sessionConversations.TryGetValue(sessionId, out var existingConversationId))
        {
            Log.Information("Resetting AgentThread: {ConversationId} for session {SessionId}", existingConversationId, sessionId);
            await threadManager.ClearThreadAsync(existingConversationId);

            var newConversationId = Guid.NewGuid().ToString();
            sessionConversations[sessionId] = newConversationId;
            Log.Information("Session {SessionId} mapped to new conversation {ConversationId}", sessionId, newConversationId);

            return "Conversation history cleared. User profiles are retained in the store.";
        }
        else
        {
            return "No active conversation to reset for this session.";
        }
    }

    // ************** End of local function definitions ************

    // Convert to MCP tools
    McpServerTool adviceTool = McpServerTool.Create(
        GetFinancialAdvice,
        new()
        {
            Name = "get_financial_advice",
            Description = "Send user messages to FinWise financial advisor. CRITICAL: The 'query' parameter is the user's EXACT message - pass it verbatim without asking for userId, email, or any other information first. DO NOT ask the user for additional details before calling this tool. Just call it immediately with whatever the user said. Examples: User says 'Give me financial advice' → immediately call with query='Give me financial advice'. User says 'I want investment help' → immediately call with query='I want investment help'. The FinWise system internally manages all user identification and profile collection through conversation. IMPORTANT: On a NEW session (new chat/conversation), the FinWise system WILL ask for the user's email address as the first step - this is expected behavior. You MUST relay the email request back to the user and then pass their email response to this tool. Do NOT skip this step or assume you know the user's email."
        });

    McpServerTool resetTool = McpServerTool.Create(
        ResetConversation,
        new()
        {
            Name = "reset_conversation",
            Description = "Clear conversation history for a user to start a fresh conversation."
        });

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "FinWise Orchestrator MCP Server",
                Version = "1.0.0"
            };
        })
        .WithHttpTransport(httpOptions =>
        {
            httpOptions.Stateless = false;
            httpOptions.IdleTimeout = TimeSpan.FromMinutes(30);
        })
        .WithTools([adviceTool, resetTool]);

    var app = builder.Build();

    httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();

    app.MapMcp("/mcp");

    Log.Information("FinWise Orchestrator MCP Server ready - listening on http://127.0.0.1:3923/mcp");
    await app.RunAsync();
}
catch (Exception ex)
{
    Infrastructure.HandleMcpServerException(ex, "Startup");
    Log.Fatal(ex, "Fatal error during MCP Server startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;





