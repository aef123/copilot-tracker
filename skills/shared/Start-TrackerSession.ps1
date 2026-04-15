[CmdletBinding()]
param(
    [string]$WorkingDirectory = $PWD,
    [string]$Repo,
    [string]$Branch,
    [string]$McpUrl = "https://copilot-tracker.azurewebsites.net"
)

# Import the module
$modulePath = Join-Path $PSScriptRoot "CopilotTracker.psm1"
if (-not (Get-Module -Name CopilotTracker)) {
    Import-Module $modulePath -Force
}

# Initialize connection
Initialize-TrackerConnection -McpUrl $McpUrl

# Start session
$sessionId = Start-TrackerSession -WorkingDirectory $WorkingDirectory -Repo $Repo -Branch $Branch

if ($sessionId) {
    Write-Host "Session tracking active: $sessionId" -ForegroundColor Green
} else {
    Write-Warning "Session tracking failed to start. Continuing without tracking."
}
