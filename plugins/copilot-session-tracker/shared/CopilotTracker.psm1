# CopilotTracker.psm1 - PowerShell module for Copilot Session Tracker
#
# All operations use the REST API (/api/*).
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

# ── REST helpers ──────────────────────────────────────────────────────

function Invoke-TrackerApi {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = "GET",
        [string]$Body
    )

    $headers = Get-TrackerHeaders
    if (-not $headers) { return $null }

    $uri = "$script:BaseUrl$Path"
    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }
    if ($Body) { $params.Body = $Body }

    return Invoke-RestMethod @params -ErrorAction Stop
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
        $body = @{ machineId = $script:MachineId }
        if ($Repo) { $body.repository = $Repo }
        if ($Branch) { $body.branch = $Branch }

        $session = Invoke-TrackerApi -Path "/api/sessions" -Method "POST" -Body ($body | ConvertTo-Json)
        if ($session.id) {
            $script:SessionId = $session.id
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
        Invoke-TrackerApi -Path "/api/sessions/$($script:MachineId)/$($script:SessionId)/heartbeat" -Method "POST" | Out-Null
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
        $body = @{}
        if ($Summary) { $body.summary = $Summary }

        Invoke-TrackerApi -Path "/api/sessions/$($script:MachineId)/$($script:SessionId)/complete" -Method "POST" -Body ($body | ConvertTo-Json) | Out-Null
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
        $body = @{
            sessionId = $script:SessionId
            queueName = $QueueName
            title     = $Title
            status    = $Status
            source    = $Source
        }
        if ($TaskId) { $body.taskId = $TaskId }
        if ($Result) { $body.result = $Result }
        if ($ErrorMessage) { $body.errorMessage = $ErrorMessage }

        $taskData = Invoke-TrackerApi -Path "/api/tasks" -Method "POST" -Body ($body | ConvertTo-Json)
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
        Invoke-TrackerApi -Path "/api/tasks/default/$TaskId/logs" -Method "POST" -Body (@{
            logType = $LogType
            message = $Message
        } | ConvertTo-Json) | Out-Null
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
                Invoke-RestMethod -Uri "$BaseUrl/api/sessions/$MachineId/$SessionId/heartbeat" -Method Post -Headers $headers -ErrorAction Stop | Out-Null
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
