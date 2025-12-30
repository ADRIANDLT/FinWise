// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to expose an AI agent as an MCP tool.

using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

// Create a local Microsoft Agent framework agent with Azure OpenAI backend
var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
IChatClient chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

AIAgent agent = chatClient.CreateAIAgent(
    instructions: "You are good at telling jokes, and you always start each joke with 'Aye aye, captain!'.",
    name: "Joker",
    description: "An agent that tells jokes.");

// Convert the agent to an AIFunction and then to an MCP tool.
// The agent name and description will be used as the mcp tool name and description.
McpServerTool tool = McpServerTool.Create(agent.AsAIFunction());

// Register the MCP server with StdIO transport and expose the tool via the server.
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools([tool]);

await builder.Build().RunAsync();
