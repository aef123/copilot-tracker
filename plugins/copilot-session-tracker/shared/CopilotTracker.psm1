# CopilotTracker.psm1 - PowerShell module for Copilot Session Tracker
#
# Writes go through the MCP endpoint (/mcp) using JSON-RPC tool calls.
# Reads use the REST API (/api/*) when available.
# The server handles all Cosmos DB access internally.
#
# Configuration is read from ~/.copilot/copilot-tracker-config.json.
# Run the 'initialize-machine' skill to create it.

$script:BaseUrl = $null
$script:SessionId = $null
$script:MachineId = $null
$script:HeartbeatJob = $null
$script:ResourceId = $null
$script:TenantId = $null
$script:ConfigLoaded = $false

function Load-TrackerConfig {
    if ($script:ConfigLoaded) { return }

    $configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"
    if (Test-Path $configPath) {
        try {
            $config = Get-Content $configPath -Raw | ConvertFrom-Json
            $script:ResourceId = $config.resourceId
            $script:TenantId = $config.tenantId
            if ($config.serverUrl -and -not $script:BaseUrl) {
                $script:BaseUrl = $config.serverUrl.TrimEnd('/')
            }
            $script:ConfigLoaded = $true
        } catch {
            Write-Warning "Session tracker: failed to read config from $configPath. Run 'initialize-machine' skill to fix."
        }
    } else {
        Write-Warning "Session tracker: no config found at $configPath. Run the 'initialize-machine' skill to set up."
    }
}

# ── Connection ────────────────────────────────────────────────────────

function Initialize-TrackerConnection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$BaseUrl
    )

    Load-TrackerConfig

    if ($BaseUrl) {
        $script:BaseUrl = $BaseUrl.TrimEnd('/')
    }

    if (-not $script:BaseUrl) {
        Write-Warning "Session tracker: no server URL configured. Run the 'initialize-machine' skill."
        return
    }
    if (-not $script:ResourceId -or -not $script:TenantId) {
        Write-Warning "Session tracker: missing resourceId or tenantId in config. Run the 'initialize-machine' skill."
        return
    }

    $script:MachineId = $env:COMPUTERNAME
    Write-Verbose "Tracker connected to $script:BaseUrl"
}

function Get-TrackerHeaders {
    [CmdletBinding()]
    param()

    $token = az account get-access-token --resource $script:ResourceId --tenant $script:TenantId --query accessToken -o tsv 2>$null
    if (-not $token) {
        Write-Warning "Session tracker: could not get a token for tenant $script:TenantId. Run 'az login --tenant $script:TenantId' to fix."
        return $null
    }
    return @{
        "Authorization" = "Bearer $token"
        "Content-Type"  = "application/json"
    }
}

# ── MCP helper (JSON-RPC tool call) ──────────────────────────────────

function Invoke-McpTool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ToolName,
        [Parameter(Mandatory)][hashtable]$Arguments
    )

    $headers = Get-TrackerHeaders
    if (-not $headers) { return $null }

    $body = @{
        jsonrpc = "2.0"
        method  = "tools/call"
        id      = [guid]::NewGuid().ToString()
        params  = @{
            name      = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Depth 5

    $response = Invoke-RestMethod -Uri "$script:BaseUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop
    if ($response.result.content) {
        return $response.result.content[0].text | ConvertFrom-Json
    }
    return $null
}

# ── REST helper (for read endpoints) ─────────────────────────────────

function Invoke-TrackerApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = "GET"
    )

    $headers = Get-TrackerHeaders
    if (-not $headers) { return $null }

    $uri = "$script:BaseUrl$Path"
    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ErrorAction Stop
}

# ── Session management ───────────────────────────────────────────────

function Start-TrackerSession {
    [CmdletBinding()]
    param(
        [string]$WorkingDirectory = $PWD,
        [string]$Repo,
        [string]$Branch
    )

    if (-not $script:BaseUrl) {
        Initialize-TrackerConnection
    }

    try {
        $args_ = @{ machineId = $script:MachineId }
        if ($Repo) { $args_.repository = $Repo }
        if ($Branch) { $args_.branch = $Branch }

        $sessionData = Invoke-McpTool -ToolName "initialize-session" -Arguments $args_
        if ($sessionData.id) {
            $script:SessionId = $sessionData.id
            Start-HeartbeatJob
            return $script:SessionId
        }
    }
    catch {
        Write-Warning "Failed to start tracker session: $_"
    }
}

function Send-TrackerHeartbeat {
    [CmdletBinding()]
    param()

    if (-not $script:SessionId -or -not $script:BaseUrl) { return }

    try {
        Invoke-McpTool -ToolName "heartbeat" -Arguments @{
            sessionId = $script:SessionId
            machineId = $script:MachineId
        } | Out-Null
    }
    catch {
        Write-Verbose "Heartbeat failed: $_"
    }
}

function Complete-TrackerSession {
    [CmdletBinding()]
    param(
        [string]$Summary
    )

    if (-not $script:SessionId -or -not $script:BaseUrl) { return }

    Stop-HeartbeatJob

    try {
        $args_ = @{
            sessionId = $script:SessionId
            machineId = $script:MachineId
        }
        if ($Summary) { $args_.summary = $Summary }

        Invoke-McpTool -ToolName "complete-session" -Arguments $args_ | Out-Null
        Write-Host "Session completed: $($script:SessionId)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed to complete session: $_"
    }

    $script:SessionId = $null
}

function Get-TrackerSession {
    [CmdletBinding()]
    param(
        [string]$SessionId = $script:SessionId,
        [string]$MachineId = $script:MachineId
    )

    if (-not $script:BaseUrl) {
        Initialize-TrackerConnection
    }

    try {
        return Invoke-TrackerApi -Path "/api/sessions/$MachineId/$SessionId"
    }
    catch {
        Write-Warning "Failed to get session: $_"
    }
}

# ── Task management ──────────────────────────────────────────────────

function Set-TrackerTask {
    [CmdletBinding()]
    param(
        [string]$TaskId,
        [string]$QueueName = "default",
        [Parameter(Mandatory)]
        [string]$Title,
        [Parameter(Mandatory)]
        [string]$Status,
        [string]$Result,
        [string]$ErrorMessage,
        [string]$Source = "prompt"
    )

    if (-not $script:SessionId -or -not $script:BaseUrl) {
        Write-Warning "No active session. Call Start-TrackerSession first."
        return
    }

    try {
        $args_ = @{
            sessionId = $script:SessionId
            queueName = $QueueName
            title     = $Title
            status    = $Status
            source    = $Source
        }
        if ($TaskId) { $args_.taskId = $TaskId }
        if ($Result) { $args_.result = $Result }
        if ($ErrorMessage) { $args_.errorMessage = $ErrorMessage }

        $taskData = Invoke-McpTool -ToolName "set-task" -Arguments $args_
        if ($taskData.id) {
            return $taskData.id
        }
    }
    catch {
        Write-Warning "Failed to set task: $_"
    }
}

function Add-TrackerLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TaskId,
        [Parameter(Mandatory)]
        [string]$LogType,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $script:BaseUrl) { return }

    try {
        Invoke-McpTool -ToolName "add-log" -Arguments @{
            taskId  = $TaskId
            logType = $LogType
            message = $Message
        } | Out-Null
    }
    catch {
        Write-Verbose "Failed to add log: $_"
    }
}

# ── Heartbeat background job ─────────────────────────────────────────

function Start-HeartbeatJob {
    Stop-HeartbeatJob

    $script:HeartbeatJob = Start-Job -ScriptBlock {
        param($BaseUrl, $SessionId, $MachineId, $ResourceId, $TenantId)
        while ($true) {
            Start-Sleep -Seconds 60
            try {
                $token = az account get-access-token --resource $ResourceId --tenant $TenantId --query accessToken -o tsv 2>$null
                if (-not $token) { continue }
                $headers = @{
                    "Authorization" = "Bearer $token"
                    "Content-Type"  = "application/json"
                }
                $body = @{
                    jsonrpc = "2.0"
                    method  = "tools/call"
                    id      = [guid]::NewGuid().ToString()
                    params  = @{
                        name      = "heartbeat"
                        arguments = @{
                            sessionId = $SessionId
                            machineId = $MachineId
                        }
                    }
                } | ConvertTo-Json -Depth 5
                Invoke-RestMethod -Uri "$BaseUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop | Out-Null
            }
            catch {
                # Best-effort; silently continue
            }
        }
    } -ArgumentList $script:BaseUrl, $script:SessionId, $script:MachineId, $script:ResourceId, $script:TenantId
}

function Stop-HeartbeatJob {
    if ($script:HeartbeatJob) {
        Stop-Job -Job $script:HeartbeatJob -ErrorAction SilentlyContinue
        Remove-Job -Job $script:HeartbeatJob -Force -ErrorAction SilentlyContinue
        $script:HeartbeatJob = $null
    }
}

# ── Lifecycle ─────────────────────────────────────────────────────────

Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action {
    if ($script:SessionId) {
        Complete-TrackerSession -Summary "Session ended (process exit)"
    }
} | Out-Null

Export-ModuleMember -Function @(
    'Initialize-TrackerConnection',
    'Start-TrackerSession',
    'Send-TrackerHeartbeat',
    'Complete-TrackerSession',
    'Get-TrackerSession',
    'Set-TrackerTask',
    'Add-TrackerLog'
)
