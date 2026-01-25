// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to expose an AI agent workflow with handoffs as an MCP tool.

using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

#if DEBUG
// Wait for debugger to attach when running in Debug mode
if (!System.Diagnostics.Debugger.IsAttached)
{
    Console.Error.WriteLine("Waiting for debugger to attach...");
    Console.Error.WriteLine($"Process ID: {Environment.ProcessId}");
    while (!System.Diagnostics.Debugger.IsAttached)
    {
        System.Threading.Thread.Sleep(100);
    }
    Console.Error.WriteLine("Debugger attached!");
}
#endif

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

// Set up the Azure OpenAI client with API key authentication
var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
IChatClient chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

// Create specialist agents for different subjects
ChatClientAgent historyTutor = new(chatClient,
    "You provide assistance with historical queries. Explain important events and context clearly. Only respond about history.",
    "history_tutor",
    "Specialist agent for historical questions");

ChatClientAgent mathTutor = new(chatClient,
    "You provide help with math problems. Explain your reasoning at each step and include examples. Only respond about math.",
    "math_tutor",
    "Specialist agent for math questions");

ChatClientAgent triageAgent = new(chatClient,
    "You determine which agent to use based on the user's homework question. ALWAYS handoff to another agent.",
    "triage_agent",
    "Routes messages to the appropriate specialist agent");

// Build the handoff workflow
Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
    .WithHandoffs(triageAgent, [mathTutor, historyTutor])
    .WithHandoffs([mathTutor, historyTutor], triageAgent)
    .Build();

// Create an AI function wrapper for the workflow
async Task<string> MCPToolToWorkflowEntryPoint(string question)
{
    List<ChatMessage> messages = [new(ChatRole.User, question)];
    await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
    await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
    
    string? lastSpecializedAgentId = null;
    
    await foreach (WorkflowEvent evt in run.WatchStreamAsync())
    {
        switch (evt)
        {
            case ExecutorInvokedEvent invoked:
                if (invoked.ExecutorId.Contains("math_tutor") || invoked.ExecutorId.Contains("history_tutor"))
                    lastSpecializedAgentId = invoked.ExecutorId;
                break;
            case WorkflowOutputEvent output:
                var outputMessages = output.As<List<ChatMessage>>();
                if (outputMessages?.Count > 0)
                {
                    var response = outputMessages[^1].Text;
                    if (!string.IsNullOrEmpty(lastSpecializedAgentId))
                        response = $"{FormatAgentName(lastSpecializedAgentId)}: {response}";
                    return response ?? string.Empty;
                }
                break;
        }
    }
    
    return string.Empty;
}

static string FormatAgentName(string agentId)
{
    if (agentId.Contains("math_tutor")) return "Math Tutor Agent";
    if (agentId.Contains("history_tutor")) return "History Tutor Agent";
    if (agentId.Contains("triage_agent")) return "Triage Agent";
    return agentId;
}

// Convert the workflow function to an MCP tool
McpServerTool tool = McpServerTool.Create(
    MCPToolToWorkflowEntryPoint,
    new()
    {
        Name = "homework_helper",
        Description = "A homework helper that routes questions to specialized tutors for math and history. The workflow intelligently hands off questions to the appropriate expert agent."
    });

// Register the MCP server with StdIO transport and expose the tool via the server.
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools([tool]);

await builder.Build().RunAsync();
