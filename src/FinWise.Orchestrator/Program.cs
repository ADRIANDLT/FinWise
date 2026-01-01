using System.ComponentModel;
using FinWise.Orchestrator;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Serilog;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

// Configure logging first
Infrastructure.ConfigureLogging();

try
{
    Log.Information("Starting FinWise Orchestrator MCP Server");

    // Generate unique session ID for this MCP server instance
    // Note: MCP stdio creates one server process per client connection
    var sessionId = Guid.NewGuid().ToString();
    Log.Information("Session ID: {SessionId}", sessionId);

    // Initialize Azure OpenAI client
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();

    // Initialize in-memory stores
    IUserProfileStore profileStore = new InMemoryUserProfileStore();
    IConversationStore conversationStore = new InMemoryConversationStore();
    Log.Information("Initialized in-memory profile and conversation stores");

    // Create specialist agents using ChatClientAgent
    ChatClientAgent orchestratorAgent = new(chatClient,
        @"You are an intelligent orchestrator that routes user queries to the most appropriate specialist agent.

Your job is to analyze the user's intent, check profile state, and route to the right agent.

═══════════════════════════════════════════════════════════════════
WORKFLOW (Execute in this exact order)
═══════════════════════════════════════════════════════════════════

STEP 1: ANALYZE USER INTENT
------------------------------------------------------------
Determine what the user is asking for by analyzing their message:

A. GENERAL/BROAD FINANCIAL ADVICE
   Keywords: ""financial advice"", ""investment advice"", ""help with money"", ""how to invest"", ""portfolio"", ""retirement planning""
   Examples: ""Give me financial advice"", ""How should I invest?"", ""What should I do with my money?""
   Target: advisor_agent (after profile check)

B. STOCK-SPECIFIC QUESTIONS  
   Keywords: ""stocks"", ""equity"", ""shares"", ""ticker"", ""stock market"", ""which stock""
   Examples: ""What stocks should I buy?"", ""Recommend some stocks"", ""Should I buy tech stocks?""
   Target: advisor_agent (stock_agent not available yet - will be added in future)

C. REAL ESTATE QUESTIONS
   Keywords: ""real estate"", ""property"", ""rental"", ""housing"", ""real estate investment""
   Examples: ""Should I invest in real estate?"", ""Buy rental property?"", ""Real estate vs stocks?""
   Target: advisor_agent (real_estate_agent not available yet - will be added in future)

D. PROFILE ANSWER (user responding to profile questions)
   Indicators: Very short answers (1-2 words), email format, ""conservative/moderate/aggressive"", 
               ""short-term/medium-term/long-term"", follows a question from profile_agent
   Examples: ""adrian@outlook.com"", ""Aggressive"", ""wealth building"", ""long-term""
   Target: profile_agent (continue collection)

E. UNCLEAR/OTHER
   Default assumption: General financial advice
   Target: advisor_agent (after profile check)

STEP 2: CHECK PROFILE STATE
------------------------------------------------------------
Search conversation history for 'PROFILE_READY:' marker:

A. PROFILE_READY FOUND:
   ✓ User has complete profile
   ✓ Proceed to STEP 3 (route to specialist)

B. PROFILE_READY NOT FOUND:
   ✗ Profile incomplete or missing
   → Check if profile_agent recently asked a question (in last 3 messages)
      • YES → Route to 'profile_agent' (user is answering, continue collection)
      • NO → Route to 'profile_agent' (start profile collection)
   → STOP - Do NOT proceed to STEP 3

STEP 3: ROUTE TO SPECIALIST (Only if PROFILE_READY found)
------------------------------------------------------------
Based on intent from STEP 1:

• Intent = General/Broad Financial Advice → Route to 'advisor_agent'
• Intent = Stocks → Route to 'advisor_agent' (stock_agent will be available in future)
• Intent = Real Estate → Route to 'advisor_agent' (real_estate_agent will be available in future)  
• Intent = Profile Answer → Route to 'profile_agent' (shouldn't happen if PROFILE_READY exists)
• Intent = Unclear → Route to 'advisor_agent'

═══════════════════════════════════════════════════════════════════
CRITICAL RULES (NEVER VIOLATE)
═══════════════════════════════════════════════════════════════════
✓ NEVER provide advice yourself - you are ONLY a router
✓ NEVER route to advisor_agent without PROFILE_READY marker in conversation
✓ If profile_agent is collecting data (asking questions), keep routing to profile_agent
✓ Profile collection must complete fully before routing to any specialist agent
✓ When uncertain about intent, default to general financial advice → advisor_agent (after profile)
✓ If uncertain about profile state, default to profile_agent (safe choice)
✓ Maintain conversation continuity - don't interrupt mid-collection

═══════════════════════════════════════════════════════════════════
ROUTING DECISION EXAMPLES (For Your Reference)
═══════════════════════════════════════════════════════════════════

Example 1 - First user message:
User: ""Give me financial advice""
Analysis: Intent=General advice, Profile=NOT FOUND
Decision: Route to 'profile_agent' (need profile first)

Example 2 - User answering profile question:
User: ""Aggressive""
Context: profile_agent just asked ""What is your risk tolerance?""
Analysis: Intent=Profile answer, profile_agent active
Decision: Route to 'profile_agent' (continue collection)

Example 3 - Complete profile, general advice:
User: ""Give me financial advice""
Context: PROFILE_READY found in history
Analysis: Intent=General advice, Profile=COMPLETE
Decision: Route to 'advisor_agent'

Example 4 - Stock question with profile:
User: ""What stocks should I buy?""
Context: PROFILE_READY found in history
Analysis: Intent=Stock-specific, Profile=COMPLETE
Decision: Route to 'advisor_agent' (stock_agent not available yet, advisor handles it)

Example 5 - Real estate question with profile:
User: ""Should I invest in real estate?""
Context: PROFILE_READY found in history
Analysis: Intent=Real estate, Profile=COMPLETE
Decision: Route to 'advisor_agent' (real_estate_agent not available yet, advisor handles it)",
        "orchestrator_agent",
        "Intelligent orchestrator that analyzes intent and routes to appropriate specialist agent");

        

    // Instantiate UserProfileAgent and create ChatClientAgent
    var profileAgentInstance = new UserProfileAgent(chatClient, profileStore);
    ChatClientAgent profileAgent = profileAgentInstance.CreateAgent();

    // AdvisorAgent - no tools, always gets profile data from ProfileAgent via handoff
    ChatClientAgent advisorAgent = new(chatClient,
        @"You provide personalized investment recommendations based on the user's profile.

MANDATORY WORKFLOW:

1. VERIFY PROFILE DATA EXISTS:
   - Search the conversation for 'PROFILE_READY:' message from profile_agent
   - Extract: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]

2. IF PROFILE_READY NOT FOUND:
   - Output: 'I need your profile information first.'
   - Handoff to 'orchestrator_agent' (orchestrator will route to profile_agent)
   - STOP - do NOT provide any recommendations

3. IF PROFILE_READY FOUND:
   - Provide personalized investment recommendations based on:
     * Risk tolerance (Conservative/Moderate/Aggressive)
     * Investment goals
     * Timeframe (Short-term/Medium-term/Long-term)
   - Explain specific risks and diversification strategies
   - Explain why recommendations fit their profile
   - End with: 'This is general guidance. Please consult a licensed financial advisor for personalized advice.'

CRITICAL RULES:
- NEVER provide recommendations without PROFILE_READY data
- NEVER ask for email or profile information yourself (handoff to orchestrator_agent)
- NEVER guess or assume profile data",
        "advisor_agent",
        "Provides investment recommendations");

    // Build the handoff workflow
    // ALL handoffs go through orchestrator - no direct agent-to-agent handoffs for scalability
    Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
        .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
        .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
        .Build();

    Log.Information("FinWise workflow initialized with 3 agents");

    // ************** Local function definitions - MCP tool implementations ************

    // Create MCP tool wrapper - Stateful conversation management
    // Conversation history persists in-memory between MCP tool calls (v0.1)
    // Will be moved to Cosmos DB in v0.3+ per architecture docs
    // Note: sessionId is used for conversation store, agents extract email for profile store
    async Task<string> GetFinancialAdvice(string query)
    {
        Log.Information("MCP Tool invoked: get_financial_advice, SessionId: {SessionId}, Query: {Query}", sessionId, query);

        try
        {
            // Simple conversation management - agents handle all business logic
            var conversationHistory = await conversationStore.GetConversationHistoryAsync(sessionId);
            Log.Information("Retrieved {MessageCount} messages for session {SessionId}", conversationHistory.Count, sessionId);

            // Add user query
            conversationHistory.Add(new ChatMessage(ChatRole.User, query));

            // Execute workflow - agents handle everything including email collection and profile management
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, conversationHistory);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            string? response = null;
            List<ChatMessage> workflowOutputs = new();

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorInvokedEvent invoked)
                    Log.Information("Agent invoked: {AgentId}", invoked.ExecutorId);
                else if (evt is WorkflowOutputEvent output)
                {
                    var messages = output.As<List<ChatMessage>>();
                    if (messages?.Count > 0)
                    {
                        response = messages[^1].Text ?? string.Empty;
                        workflowOutputs.AddRange(messages);
                    }
                }
            }

            // Add outputs to history
            conversationHistory.AddRange(workflowOutputs);

            // Persist conversation
            await conversationStore.SetConversationHistoryAsync(sessionId, conversationHistory);
            Log.Information("Persisted {MessageCount} messages for session {SessionId}", conversationHistory.Count, sessionId);

            return response ?? "No response generated.";
        }
        catch (Exception ex)
        {
            Infrastructure.HandleMcpServerException(ex, "GetFinancialAdvice");
            return "I apologize, but I encountered an error processing your request. Please try again.";
        }
    }

    // Tool to reset conversation history (keeps profile in store)
    async Task<string> ResetConversation()
    {
        Log.Information("Resetting conversation for session: {SessionId}", sessionId);
        await conversationStore.ClearConversationAsync(sessionId);
        
        // Note: Profiles are stored by email (extracted by agents), not by session ID
        // We cannot check profile existence without knowing the email
        return $"Conversation history cleared for session '{sessionId}'. User profiles (if any) are retained.";
    }

    // ************** End of local function definitions ************

    // Convert to MCP tools
    McpServerTool adviceTool = McpServerTool.Create(
        GetFinancialAdvice,
        new()
        {
            Name = "get_financial_advice",
            Description = "Send user messages to FinWise financial advisor. CRITICAL: The 'query' parameter is the user's EXACT message - pass it verbatim without asking for userId, email, or any other information first. DO NOT ask the user for additional details before calling this tool. Just call it immediately with whatever the user said. Examples: User says 'Give me financial advice' → immediately call with query='Give me financial advice'. User says 'I want investment help' → immediately call with query='I want investment help'. The FinWise system internally manages all user identification and profile collection through conversation."
        });

    McpServerTool resetTool = McpServerTool.Create(
        ResetConversation,
        new()
        {
            Name = "reset_conversation",
            Description = "Clear conversation history for a user to start a fresh conversation session."
        });

    // Register the MCP server with StdIO transport
    HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools([adviceTool, resetTool]);

    Log.Information("FinWise Orchestrator MCP Server ready - listening on STDIO");
    await builder.Build().RunAsync();
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





