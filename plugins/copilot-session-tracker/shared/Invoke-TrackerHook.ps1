<#
.SYNOPSIS
    Universal Copilot CLI hook handler for session tracking.
.DESCRIPTION
    Called by hooks.json for each hook event. Reads JSON from stdin,
    enriches the payload, acquires a token, and POSTs to the tracker server.
.PARAMETER HookType
    The type of hook event (sessionStart, sessionEnd, userPromptSubmitted, etc.)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$HookType
)

$ErrorActionPreference = "Stop"

try {
    # Read stdin (hook payload from Copilot CLI)
    $inputJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputJson)) { exit 0 }
    
    $payload = $inputJson | ConvertFrom-Json
    
    # Load config
    $configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"
    if (-not (Test-Path $configPath)) { exit 0 }
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    
    if (-not $config.serverUrl) { exit 0 }
    
    # Load token helper
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    . (Join-Path $scriptDir "Get-TrackerToken.ps1")
    
    # Acquire token
    $token = Get-TrackerToken -Config $config
    
    # Enrich payload with machine info
    $payload | Add-Member -NotePropertyName "machineName" -NotePropertyValue $env:COMPUTERNAME -Force
    
    # For sessionStart: add git info from CWD
    if ($HookType -eq "sessionStart" -and $payload.cwd) {
        try {
            $repo = & git -C $payload.cwd remote get-url origin 2>$null
            if ($repo) { $payload | Add-Member -NotePropertyName "repository" -NotePropertyValue $repo -Force }
        } catch { }
        try {
            $branch = & git -C $payload.cwd branch --show-current 2>$null
            if ($branch) { $payload | Add-Member -NotePropertyName "branch" -NotePropertyValue $branch -Force }
        } catch { }
    }
    
    # For postToolUse: only send a lightweight heartbeat
    if ($HookType -eq "postToolUse") {
        $payload = @{
            sessionId   = $payload.sessionId
            timestamp   = $payload.timestamp
            machineName = $env:COMPUTERNAME
        }
    }
    
    # POST to server
    $body = $payload | ConvertTo-Json -Depth 10 -Compress
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type"  = "application/json"
    }
    $uri = "$($config.serverUrl)/api/hooks/$HookType"
    
    Invoke-RestMethod -Uri $uri -Method POST -Headers $headers -Body $body -TimeoutSec 10 | Out-Null
    
    exit 0
} catch {
    # Best-effort: never block the agent
    try {
        $errDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"
        if (-not (Test-Path $errDir)) { New-Item -ItemType Directory -Path $errDir -Force | Out-Null }
        $errPath = Join-Path $errDir "hook-errors.log"
        $errMsg = "$(Get-Date -Format 'o'): [$HookType] $($_.Exception.Message)"
        Add-Content -Path $errPath -Value $errMsg -ErrorAction SilentlyContinue
    } catch { }
    exit 0
}
