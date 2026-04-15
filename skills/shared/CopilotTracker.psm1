# CopilotTracker.psm1 - PowerShell module for Copilot Session Tracker
# Talks to the MCP server (not Cosmos DB directly)

$script:McpUrl = $null
$script:SessionId = $null
$script:MachineId = $null
$script:HeartbeatJob = $null

function Initialize-TrackerConnection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$McpUrl = "https://copilot-tracker.azurewebsites.net"
    )

    $script:McpUrl = $McpUrl.TrimEnd('/')
    $script:MachineId = $env:COMPUTERNAME
    Write-Verbose "Tracker connected to $script:McpUrl"
}

function Get-McpHeaders {
    # Get an access token for the API
    $token = az account get-access-token --resource "api://4c8148f5-c913-40c5-863f-1c019821eac4" --query accessToken -o tsv 2>$null
    if (-not $token) {
        Write-Warning "Failed to get access token. Run 'az login' first."
        return $null
    }
    return @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    }
}

function Start-TrackerSession {
    [CmdletBinding()]
    param(
        [string]$WorkingDirectory = $PWD,
        [string]$Repo,
        [string]$Branch
    )

    if (-not $script:McpUrl) {
        Initialize-TrackerConnection
    }

    $headers = Get-McpHeaders
    if (-not $headers) { return }

    # Call MCP initialize-session tool
    $body = @{
        jsonrpc = "2.0"
        method = "tools/call"
        id = [guid]::NewGuid().ToString()
        params = @{
            name = "initialize-session"
            arguments = @{
                machineId = $script:MachineId
                repository = $Repo
                branch = $Branch
            }
        }
    } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod -Uri "$script:McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop
        $result = $response.result

        if ($result.content) {
            $sessionData = $result.content[0].text | ConvertFrom-Json
            $script:SessionId = $sessionData.id
            Write-Host "Session tracking active: $($script:SessionId)" -ForegroundColor Green

            # Start heartbeat background job
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

    if (-not $script:SessionId -or -not $script:McpUrl) { return }

    $headers = Get-McpHeaders
    if (-not $headers) { return }

    $body = @{
        jsonrpc = "2.0"
        method = "tools/call"
        id = [guid]::NewGuid().ToString()
        params = @{
            name = "heartbeat"
            arguments = @{
                sessionId = $script:SessionId
                machineId = $script:MachineId
            }
        }
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod -Uri "$script:McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop | Out-Null
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

    if (-not $script:SessionId -or -not $script:McpUrl) { return }

    # Stop heartbeat
    Stop-HeartbeatJob

    $headers = Get-McpHeaders
    if (-not $headers) { return }

    $body = @{
        jsonrpc = "2.0"
        method = "tools/call"
        id = [guid]::NewGuid().ToString()
        params = @{
            name = "complete-session"
            arguments = @{
                sessionId = $script:SessionId
                machineId = $script:MachineId
                summary = $Summary
            }
        }
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod -Uri "$script:McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop | Out-Null
        Write-Host "Session completed: $($script:SessionId)" -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed to complete session: $_"
    }

    $script:SessionId = $null
}

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

    if (-not $script:SessionId -or -not $script:McpUrl) {
        Write-Warning "No active session. Call Start-TrackerSession first."
        return
    }

    $headers = Get-McpHeaders
    if (-not $headers) { return }

    $arguments = @{
        sessionId = $script:SessionId
        queueName = $QueueName
        title = $Title
        status = $Status
        source = $Source
    }
    if ($TaskId) { $arguments.taskId = $TaskId }
    if ($Result) { $arguments.result = $Result }
    if ($ErrorMessage) { $arguments.errorMessage = $ErrorMessage }

    $body = @{
        jsonrpc = "2.0"
        method = "tools/call"
        id = [guid]::NewGuid().ToString()
        params = @{
            name = "set-task"
            arguments = $arguments
        }
    } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod -Uri "$script:McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop
        if ($response.result.content) {
            $taskData = $response.result.content[0].text | ConvertFrom-Json
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

    if (-not $script:McpUrl) { return }

    $headers = Get-McpHeaders
    if (-not $headers) { return }

    $body = @{
        jsonrpc = "2.0"
        method = "tools/call"
        id = [guid]::NewGuid().ToString()
        params = @{
            name = "add-log"
            arguments = @{
                taskId = $TaskId
                logType = $LogType
                message = $Message
            }
        }
    } | ConvertTo-Json -Depth 5

    try {
        Invoke-RestMethod -Uri "$script:McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop | Out-Null
    }
    catch {
        Write-Verbose "Failed to add log: $_"
    }
}

function Start-HeartbeatJob {
    Stop-HeartbeatJob  # Clean up any existing job

    $script:HeartbeatJob = Start-Job -ScriptBlock {
        param($McpUrl, $SessionId, $MachineId, $ResourceId)
        while ($true) {
            Start-Sleep -Seconds 60
            try {
                $token = az account get-access-token --resource $ResourceId --query accessToken -o tsv 2>$null
                if (-not $token) { continue }
                $headers = @{
                    "Authorization" = "Bearer $token"
                    "Content-Type" = "application/json"
                }
                $body = @{
                    jsonrpc = "2.0"
                    method = "tools/call"
                    id = [guid]::NewGuid().ToString()
                    params = @{
                        name = "heartbeat"
                        arguments = @{
                            sessionId = $SessionId
                            machineId = $MachineId
                        }
                    }
                } | ConvertTo-Json -Depth 5
                Invoke-RestMethod -Uri "$McpUrl/mcp" -Method Post -Headers $headers -Body $body -ErrorAction Stop | Out-Null
            }
            catch {
                # Silently continue - heartbeat is best-effort
            }
        }
    } -ArgumentList $script:McpUrl, $script:SessionId, $script:MachineId, "api://4c8148f5-c913-40c5-863f-1c019821eac4"
}

function Stop-HeartbeatJob {
    if ($script:HeartbeatJob) {
        Stop-Job -Job $script:HeartbeatJob -ErrorAction SilentlyContinue
        Remove-Job -Job $script:HeartbeatJob -Force -ErrorAction SilentlyContinue
        $script:HeartbeatJob = $null
    }
}

# Register exit handler to complete session on shutdown
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
    'Set-TrackerTask',
    'Add-TrackerLog'
)
