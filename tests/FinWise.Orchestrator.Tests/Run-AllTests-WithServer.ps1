<#
Runs the full test suite, starting the MCP server automatically if needed.

How to run:
    powershell -NoProfile -ExecutionPolicy Bypass -File "c:\Users\cesardl\git-repos\finwise-ces-baseline-agents-workflow-impl\tests\FinWise.Orchestrator.Tests\Run-AllTests-WithServer.ps1"

How to run ONLY the E2E MCP tests:
    powershell -NoProfile -ExecutionPolicy Bypass -File "c:\Users\cesardl\git-repos\finwise-ces-baseline-agents-workflow-impl\tests\FinWise.Orchestrator.Tests\Run-AllTests-WithServer.ps1" -E2EOnly

How to run with custom server URL and E2E tests only:
    powershell -NoProfile -ExecutionPolicy Bypass -File "c:\Users\cesardl\git-repos\finwise-ces-baseline-agents-workflow-impl\tests\FinWise.Orchestrator.Tests\Run-AllTests-WithServer.ps1" -ServerUrl http://127.0.0.1:3923 -E2EOnly

Optional parameters:
    -ServerUrl http://127.0.0.1:3923
    -StartupTimeoutSeconds 30
    -E2EOnly
#>

[CmdletBinding()]
param(
    [string]$ServerUrl = "http://127.0.0.1:3923",
    [int]$StartupTimeoutSeconds = 30,
    [switch]$E2EOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ServerReady {
    param([string]$Url)

    try {
        $uri = [Uri]$Url
        $port = if ($uri.Port -gt 0) { $uri.Port } else { 80 }
        return (Test-NetConnection -ComputerName $uri.Host -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue)
    }
    catch {
        return $false
    }
}

function Get-ListeningPid {
    param([string]$Url)

    try {
        $uri = [Uri]$Url
        $port = if ($uri.Port -gt 0) { $uri.Port } else { 80 }
        $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $conn) {
            return [int]$conn.OwningProcess
        }
    }
    catch {
        # ignore
    }

    return $null
}

function Test-IsOrchestratorProcess {
    param([int]$Pid)

    try {
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$Pid" -ErrorAction Stop
        if ($null -eq $proc) { return $false }

        $cmd = [string]$proc.CommandLine
        if ([string]::IsNullOrWhiteSpace($cmd)) { return $false }

        return (
            $cmd -match 'FinWise\.Orchestrator' -or
            $cmd -match 'FinWise\.Orchestrator\.csproj' -or
            $cmd -match '--urls\s+http://127\.0\.0\.1:3923'
        )
    }
    catch {
        return $false
    }
}

function Wait-ServerReady {
    param(
        [string]$Url,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-ServerReady -Url $Url) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Get-ChildProcessIds {
    param([int]$ParentId)

    try {
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ParentId" -ErrorAction Stop
        return @($children | ForEach-Object { [int]$_.ProcessId })
    }
    catch {
        return @()
    }
}

function Get-DescendantProcessIds {
    param([int]$RootId)

    $all = New-Object System.Collections.Generic.HashSet[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootId)

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($child in (Get-ChildProcessIds -ParentId $current)) {
            if ($all.Add($child)) {
                $queue.Enqueue($child)
            }
        }
    }

    return @($all)
}

function Stop-ProcessTree {
    param(
        [int]$RootId,
        [int]$TimeoutSeconds = 10
    )

    $descendants = Get-DescendantProcessIds -RootId $RootId
    $allIds = @($descendants + $RootId) | Select-Object -Unique

    foreach ($procId in ($allIds | Sort-Object -Descending)) {
        try { Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue } catch { }
    }

    try {
        Wait-Process -Id $allIds -Timeout $TimeoutSeconds -ErrorAction SilentlyContinue
    }
    catch {
        # best-effort
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$orchestratorDir = Join-Path $repoRoot "src\FinWise.Orchestrator"

$serverProcess = $null
$startedServer = $false
$preExistingListeningPid = $null

try {
    $preExistingListeningPid = Get-ListeningPid -Url $ServerUrl

    # Avoid Windows file locking issues by building once up-front, then running server/tests with --no-build.
    Push-Location $repoRoot
    try {
        Write-Host "Building repo (dotnet build)..." -ForegroundColor Cyan
        dotnet build
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    if ($null -eq $preExistingListeningPid -and -not (Test-ServerReady -Url $ServerUrl)) {
        Write-Host "MCP server not detected at $ServerUrl. Starting it in the background..." -ForegroundColor Cyan

        $serverProcess = Start-Process dotnet `
            -WorkingDirectory $orchestratorDir `
            -ArgumentList @(
                "run",
                "--no-build",
                "--project",
                "FinWise.Orchestrator.csproj",
                "--urls",
                $ServerUrl
            ) `
            -PassThru

        $startedServer = $true

        if (-not (Wait-ServerReady -Url $ServerUrl -TimeoutSeconds $StartupTimeoutSeconds)) {
            throw "Server did not become ready at $ServerUrl within $StartupTimeoutSeconds seconds."
        }

        Write-Host "MCP server is listening at $ServerUrl." -ForegroundColor Green
    }
    else {
        Write-Host "MCP server already listening at $ServerUrl (PID $preExistingListeningPid). Will not start/stop it." -ForegroundColor Yellow
    }

    Push-Location $repoRoot
    try {
        if ($E2EOnly) {
            Write-Host "Running E2E functional tests (EndToEndMcpTests)..." -ForegroundColor Cyan
            dotnet test tests\FinWise.Orchestrator.Tests\FinWise.Orchestrator.Tests.csproj --filter "FullyQualifiedName~EndToEndMcpTests" --verbosity normal --no-build
        }
        else {
            Write-Host "Running full test suite (dotnet test) from repo root..." -ForegroundColor Cyan
            dotnet test --verbosity normal --no-build
        }

        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($startedServer -and $serverProcess -ne $null) {
        Write-Host "Stopping MCP server process tree (PID $($serverProcess.Id))..." -ForegroundColor Cyan
        try {
            Stop-ProcessTree -RootId $serverProcess.Id -TimeoutSeconds 15
        }
        catch {
            # best-effort
        }

        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            if (-not (Test-ServerReady -Url $ServerUrl)) {
                break
            }
            Start-Sleep -Milliseconds 250
        }

        if (Test-ServerReady -Url $ServerUrl) {
            $listeningPid = Get-ListeningPid -Url $ServerUrl

            # Important safety rule:
            # Only try to kill the port-owning PID when there was no listener at script start
            # (meaning: if something is listening now, it's extremely likely to be what we started).
            if ($null -eq $preExistingListeningPid -and $null -ne $listeningPid -and (Test-IsOrchestratorProcess -Pid $listeningPid)) {
                Write-Host "Server still listening; stopping port-owning process (PID $listeningPid)..." -ForegroundColor Yellow
                try { Stop-Process -Id $listeningPid -Force -ErrorAction SilentlyContinue } catch { }
                Start-Sleep -Seconds 1
            }

            if (Test-ServerReady -Url $ServerUrl) {
                Write-Host "Warning: MCP server still appears to be listening at $ServerUrl after shutdown attempts." -ForegroundColor Yellow
            }
        }
    }
}
