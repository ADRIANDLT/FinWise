# FinWise Orchestrator MCP Server

A Model Context Protocol (MCP) server for the FinWise orchestrator, providing financial tools and services through standardized AI integrations.

## Features

- **Random Number Generation**: Test MCP connectivity with random number generation
- **Welcome Messages**: Get personalized welcome messages from the orchestrator
- **Extensible Architecture**: Built on the official Microsoft MCP Server template for .NET 10

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet) (preview 6 or higher)
- [Visual Studio Code](https://code.visualstudio.com/) (recommended)
- [GitHub Copilot extension](https://marketplace.visualstudio.com/items?itemName=GitHub.copilot) for testing

## Getting Started

### Building the Server

```bash
dotnet build
```

### Running the Server

```bash
dotnet run
```

### Testing with VS Code

1. Open this workspace in VS Code
2. The server is pre-configured in `.vscode/mcp.json`
3. Open GitHub Copilot and switch to agent mode
4. Select the tools icon to verify "FinWise-Orchestrator-MCP" is available
5. Try prompts like:
   - "Give me a random number between 1 and 100"
   - "Show me a welcome message"

## Publishing to NuGet

### Pack the Project

```bash
dotnet pack -c Release
```

### Publish to NuGet

```bash
dotnet nuget push bin/Release/*.nupkg --api-key <your-api-key> --source https://api.nuget.org/v3/index.json
```

## Configuration

The server uses `appsettings.json` for configuration and supports environment variables for deployment scenarios.

## Project Structure

```
src/finwise-orchestrator-mcp-server/
├── .mcp/
│   └── server.json          # MCP server metadata
├── Tools/
│   └── FinWiseTools.cs      # Tool implementations
├── Program.cs               # Server entry point
├── appsettings.json         # Configuration
└── finwise-orchestrator-mcp-server.csproj
```

## License

See [LICENSE](../../LICENSE) for details.

## Contributing

This project is part of the FinWise capstone project. See the main repository README for contribution guidelines.
