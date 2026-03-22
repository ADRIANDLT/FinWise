using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using FinWise.MultiAgentWorkflow.Session;
using Serilog;

namespace FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent;

public class OrchestratorAgentFactory
{
    private readonly IChatClient _chatClient;

    public string Prompt { get; } = LoadPrompt();

    // Prompt loaded from embedded .prompt.md resource for editor-friendly editing,
    // clean git diffs on prompt changes, and separation of prompt engineering from C# code.
    private static string LoadPrompt()
    {
        var assembly = typeof(OrchestratorAgentFactory).Assembly;
        const string resourceName = "FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent.OrchestratorAgent.prompt.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string Name => "orchestrator_agent";
    public string Description => "Silent router - calls handoff functions only, never outputs text";

    public OrchestratorAgentFactory(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    [Description("Request a session reset. Call this when the user wants to start over, re-identify, switch user, or begin a new conversation. After calling this, respond directly with a reset confirmation — do NOT hand off.")]
    private static string RequestSessionReset()
    {
        SessionResetFlag.Current?.Request();
        Log.Information("Session reset requested by orchestrator tool");
        return "SESSION_RESET_REQUESTED. Respond directly to the user confirming the reset and asking for their email. Do NOT hand off to any agent.";
    }

    public ChatClientAgent CreateAgent()
    {
        var tools = new AIFunction[]
        {
            AIFunctionFactory.Create(RequestSessionReset, name: "request_session_reset")
        };

        // Id must be stable and deterministic — the SDK's InMemoryAgentSessionStore
        // keys sessions by {agentId}:{conversationId}. A random Id (the default)
        // would lose sessions between requests.
        return new ChatClientAgent(_chatClient, new ChatClientAgentOptions
        {
            Id = Name,
            Name = Name,
            Description = Description,
            ChatOptions = new() { Instructions = Prompt, Tools = [.. tools] }
        });
    }
}