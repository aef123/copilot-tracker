[CmdletBinding()]
param(
    [string]$WorkingDirectory = $PWD,
    [string]$Repo,
    [string]$Branch,
    [string]$BaseUrl
)

$ErrorActionPreference = "Continue"

# Import the module
$modulePath = Join-Path $PSScriptRoot "CopilotTracker.psm1"
if (-not (Get-Module -Name CopilotTracker)) {
    Import-Module $modulePath -Force
}

# Initialize connection (BaseUrl falls through to env var / default inside the module)
$initParams = @{}
if ($BaseUrl) { $initParams.BaseUrl = $BaseUrl }
Initialize-TrackerConnection @initParams

# Clean up any stale sessions from a previous crash
$staleJobPrefix = "TrackerHeartbeat_"
Get-Job -Name "$staleJobPrefix*" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Cleaned up stale session: $($_.Name -replace $staleJobPrefix, '')"
    Stop-Job $_ -ErrorAction SilentlyContinue
    Remove-Job $_ -Force -ErrorAction SilentlyContinue
}

# Start session
try {
    $sessionId = Start-TrackerSession -WorkingDirectory $WorkingDirectory -Repo $Repo -Branch $Branch

    if ($sessionId) {
        Write-Host "Session tracking active: $sessionId" -ForegroundColor Green
    } else {
        Write-Warning "Session tracking failed to start. Continuing without tracking."
    }
}
catch {
    Write-Warning "Session tracking failed: $_. Continuing without tracking."
}
