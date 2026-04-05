# ============================================================
# Stage 1: Build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first (layer caching for restore)
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/FinWise.McpServer/FinWise.McpServer.csproj src/FinWise.McpServer/
COPY src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj src/FinWise.MultiAgentWorkflow/

# Restore NuGet packages as distinct layer (restore the server project, which
# transitively restores MultiAgentWorkflow — avoids needing the full .slnx
# that references test projects not present in the Docker context)
RUN dotnet restore src/FinWise.McpServer/FinWise.McpServer.csproj

# Copy remaining source code
COPY src/ src/

# Publish the MCP server
WORKDIR /source/src/FinWise.McpServer
RUN dotnet publish -c Release -o /app --no-restore

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for Docker health checks (before switching to non-root user)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Non-root user (default 'app' user in aspnet:10.0, UID 1654)
USER $APP_UID

COPY --from=build /app .

# FinWise explicitly configures Kestrel to port 5000 (via appsettings.Docker.json),
# which takes precedence over the aspnet image default of 8080.
EXPOSE 5000

ENTRYPOINT ["dotnet", "FinWise.McpServer.dll"]
