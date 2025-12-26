# Agent Workflow Handoff as MCP Tool

This sample demonstrates how to expose a Microsoft Agent Framework workflow with handoffs as an MCP tool. The workflow includes:

- **Triage Agent**: Routes incoming homework questions to the appropriate specialist
- **Math Tutor**: Handles mathematics questions with step-by-step explanations
- **History Tutor**: Provides assistance with historical queries and context

The workflow uses the handoff pattern to intelligently route questions between agents based on the subject matter.

## Prerequisites

- .NET 10.0 SDK
- Azure OpenAI deployment with access to a chat model (e.g., gpt-4o-mini)
- Azure OpenAI API key

## Environment Variables

Set the following environment variables:

- `AZURE_OPENAI_ENDPOINT`: Your Azure OpenAI endpoint URL (e.g., `https://your-resource.openai.azure.com/`)
- `AZURE_OPENAI_DEPLOYMENT_NAME`: Your model deployment name (e.g., `gpt-4o-mini`)
- `AZURE_OPENAI_API_KEY`: Your Azure OpenAI API key

## Run the sample

To run the sample, use one of the following MCP clients: https://modelcontextprotocol.io/clients

Alternatively, use the QuickstartClient sample from the MCP C# SDK repository.

## Run the sample using MCP Inspector

To use the [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector), follow these steps:

1. Open a terminal in the AgentWorkflowHandOff_AsMcpTool project directory.

2. Run the following command to start the MCP Inspector. Make sure you have [node.js](https://nodejs.org/en/download/) and npm installed.
   ```bash
   npx @modelcontextprotocol/inspector dotnet run
   ```

3. When the inspector is running, it will display a URL in the terminal, like this:
   ```
   MCP Inspector is up and running at http://127.0.0.1:6274
   ```

4. Open a web browser and navigate to the URL displayed in the terminal.

5. In the MCP Inspector interface, add the following environment variables:
    - `AZURE_OPENAI_ENDPOINT`: Your Azure OpenAI endpoint (e.g., `https://your-resource.openai.azure.com/`)
    - `AZURE_OPENAI_DEPLOYMENT_NAME`: Your model deployment name (e.g., `gpt-4o-mini`)
    - `AZURE_OPENAI_API_KEY`: Your Azure OpenAI API key

6. Click the `Connect` button to connect to the MCP server.

7. Once connected, open the `Tools` tab and select the `homework_helper` tool.

8. Try these example questions:
   - **Math**: "What is the derivative of x squared?"
   - **History**: "Who was the first president of the United States?"
   - **Math**: "Solve the equation 2x + 5 = 13"
   - **History**: "What caused World War I?"

9. The workflow will automatically route each question to the appropriate specialist tutor and return their response.

## How it works

1. The triage agent receives the user's question
2. It analyzes the question to determine the subject matter
3. It hands off to either the math tutor or history tutor
4. The specialist agent processes the question
5. The response is returned through the MCP tool interface

The workflow demonstrates the power of combining Microsoft Agent Framework's handoff pattern with MCP's tool interface, allowing AI assistants to access sophisticated multi-agent workflows as simple tools.
