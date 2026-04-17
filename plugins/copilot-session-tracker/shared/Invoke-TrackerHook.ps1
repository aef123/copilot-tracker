<#
.SYNOPSIS
    Universal hook handler for session tracking (Copilot CLI and Claude Code).
.DESCRIPTION
    Called by hooks.json (Copilot CLI) or Claude Code hooks for each hook event.
    Reads JSON from stdin, normalizes the payload format, enriches it,
    acquires a token, and POSTs to the tracker server.

    Claude Code sends snake_case fields (session_id, transcript_path, etc.)
    while Copilot CLI sends camelCase (sessionId, transcriptPath).
    The -Tool parameter controls which normalization path runs.
.PARAMETER HookType
    The API hook type (sessionStart, sessionEnd, userPromptSubmitted, etc.)
.PARAMETER Tool
    The tool that triggered the hook: "copilot" or "claude". Defaults to "copilot".
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$HookType,

    [Parameter(Mandatory = $false)]
    [string]$Tool = "copilot"
)

$ErrorActionPreference = "Stop"

try {
    # Read stdin (hook payload)
    $inputJson = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($inputJson)) { exit 0 }
    
    $payload = $inputJson | ConvertFrom-Json
    
    # Normalize Claude Code payload to API format
    # Claude sends snake_case fields and doesn't include a timestamp
    if ($Tool -eq "claude") {
        $normalized = @{}
        
        # Core fields present in all Claude hooks
        $sid = if ($payload.session_id) { $payload.session_id } elseif ($payload.sessionId) { $payload.sessionId } else { "" }
        $normalized.sessionId = $sid
        if ($payload.cwd) { $normalized.cwd = $payload.cwd }
        
        # Claude doesn't send a unix-ms timestamp; synthesize one
        $normalized.timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        
        # Event-specific fields
        if ($payload.source) { $normalized.source = $payload.source }
        if ($payload.prompt) { $normalized.prompt = $payload.prompt }
        if ($payload.transcript_path) { $normalized.transcriptPath = $payload.transcript_path }
        
        # Stop hook: Claude may not send stop_reason
        if ($payload.stop_reason) { $normalized.stopReason = $payload.stop_reason }
        elseif ($HookType -eq "agentStop") { $normalized.stopReason = "end_turn" }
        
        # SessionEnd: Claude may not send reason
        if ($payload.reason) { $normalized.reason = $payload.reason }
        elseif ($HookType -eq "sessionEnd") { $normalized.reason = "session_end" }
        
        # Subagent hooks: agent_type maps to agentName
        if ($payload.agent_type) { $normalized.agentName = $payload.agent_type }
        if ($payload.agent_id) { $normalized.agentDisplayName = $payload.agent_id }
        
        # Notification fields
        if ($payload.message) { $normalized.message = $payload.message }
        if ($payload.title) { $normalized.title = $payload.title }
        if ($payload.notification_type) { $normalized.notificationType = $payload.notification_type }
        if ($payload.hook_event_name) { $normalized.hookEventName = $payload.hook_event_name }
        
        $payload = [PSCustomObject]$normalized
    }
    
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
    $payload | Add-Member -NotePropertyName "tool" -NotePropertyValue $Tool -Force
    
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
            timestamp   = if ($payload.timestamp) { $payload.timestamp } else { [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
            machineName = $env:COMPUTERNAME
            tool        = $Tool
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
