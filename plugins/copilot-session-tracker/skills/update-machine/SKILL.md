---
name: update-machine
description: "Update an existing AI Session Tracker installation to the current plugin version. Preserves configuration and only asks about new options."
argument-hint: ""
compatibility: "Windows only. Requires PowerShell 7+. Requires a prior initialize-machine setup."
metadata:
  author: Copilot Session Tracker
  version: 4.0.0
  category: setup
---

# Update Machine for AI Session Tracker

This skill updates an existing AI Session Tracker installation to the current plugin version (4.0.0). It reads the existing configuration, updates hook scripts and hooks configuration, and verifies everything works. It does NOT ask for configuration that already exists.

**Platform: Windows only** (uses `$env:USERPROFILE`, Windows-style paths, PowerShell 7+).

## Step 0: Read Existing Configuration

Read the existing config file. If it doesn't exist, the user needs to run initialize-machine first.

```powershell
$configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"

if (-not (Test-Path $configPath)) {
    Write-Error "❌ No existing configuration found at $configPath"
    Write-Error "   Run the initialize-machine skill first to set up this machine."
    return
}

try {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json -AsHashtable
} catch {
    Write-Error "❌ Config file is corrupted: $_"
    Write-Error "   Back up and delete $configPath, then run initialize-machine to reconfigure."
    return
}

# Validate required fields
$requiredFields = @("serverUrl", "tenantId", "resourceId", "authMode")
$missing = $requiredFields | Where-Object { -not $config.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($config[$_]) }
if ($missing) {
    Write-Error "❌ Config is missing required fields: $($missing -join ', ')"
    Write-Error "   Run initialize-machine to reconfigure from scratch."
    return
}

$previousVersion = if ($config.ContainsKey("installedVersion")) { $config["installedVersion"] } else { "unknown (pre-4.0.0)" }

Write-Output "📋 Current configuration:"
Write-Output "   Server URL:    $($config['serverUrl'])"
Write-Output "   Tenant ID:     $($config['tenantId'])"
Write-Output "   Resource ID:   $($config['resourceId'])"
Write-Output "   Auth Mode:     $($config['authMode'])"
Write-Output "   Installed Ver: $previousVersion"
if ($config['authMode'] -eq "certificate") {
    Write-Output "   Client ID:     $($config['clientId'])"
    Write-Output "   Cert Subject:  $($config['certificateSubject'])"
}
```

## Step 1: Detect Installed Tools

Scan both hook config files to determine which tools have tracker hooks installed. Don't rely solely on runtime environment detection. A machine may have both Claude and Copilot configured.

```powershell
$installedTools = @()

# Check Copilot CLI hooks
$copilotHooksPath = Join-Path $env:USERPROFILE ".copilot\hooks\hooks.json"
if (Test-Path $copilotHooksPath) {
    try {
        $copilotHooks = Get-Content $copilotHooksPath -Raw
        if ($copilotHooks -match "Invoke-TrackerHook") {
            $installedTools += "copilot"
            Write-Output "🔍 Found tracker hooks in Copilot CLI ($copilotHooksPath)"
        }
    } catch {
        Write-Warning "⚠️  Could not read $copilotHooksPath"
    }
}

# Check Claude Code hooks
$claudeSettingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
if (Test-Path $claudeSettingsPath) {
    try {
        $claudeSettings = Get-Content $claudeSettingsPath -Raw
        if ($claudeSettings -match "Invoke-TrackerHook") {
            $installedTools += "claude"
            Write-Output "🔍 Found tracker hooks in Claude Code ($claudeSettingsPath)"
        }
    } catch {
        Write-Warning "⚠️  Could not read $claudeSettingsPath"
    }
}

# If no hooks found in either, detect from current environment
if ($installedTools.Count -eq 0) {
    Write-Warning "⚠️  No existing tracker hooks found in either tool."
    if ($env:CLAUDE_PROJECT_DIR) {
        $installedTools += "claude"
        Write-Output "🔍 Detected Claude Code from environment. Will configure hooks for Claude."
    } else {
        $installedTools += "copilot"
        Write-Output "🔍 Defaulting to Copilot CLI. Will configure hooks for Copilot."
    }
}

if ($installedTools.Count -gt 1) {
    Write-Output "🔍 Both Claude Code and Copilot CLI have tracker hooks. Will update both."
}
```

Ask the user to confirm: **"I'll update tracker hooks for: [tools]. Proceed? (yes/no)"**

**Do NOT proceed until the user confirms.**

## Step 2: Check for New Configuration Options

Compare existing config against what the current version expects. If the current version introduced new required options that aren't in the config, ask for them here.

```powershell
# Current version: 4.0.0
# Expected config keys for 4.0.0:
$expectedKeys = @("serverUrl", "tenantId", "resourceId", "authMode", "installedVersion")
# Certificate mode also expects: clientId, certificateSubject, certificateThumbprint

$newKeys = $expectedKeys | Where-Object { -not $config.ContainsKey($_) }

# installedVersion is managed by the skill, not user-provided
$userNewKeys = $newKeys | Where-Object { $_ -ne "installedVersion" }

if ($userNewKeys.Count -gt 0) {
    Write-Output "ℹ️  New configuration options in this version: $($userNewKeys -join ', ')"
    # Ask the user for each new option
    # For now (4.0.0), there are no new user-facing options
}

Write-Output "✅ Configuration is up to date. No new options required."
```

**If there are new user-facing options**, ask the user for each one using the same question format as initialize-machine.

## Step 3: Backup Existing Hook Scripts

Before overwriting, create backups of the current scripts so the user can roll back if needed.

```powershell
$trackerDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"
$backupDir = Join-Path $trackerDir "backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

if (Test-Path $trackerDir) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    Get-ChildItem $trackerDir -File -Filter "*.ps1" | ForEach-Object {
        Copy-Item $_.FullName -Destination $backupDir -Force
    }
    Write-Output "✅ Backed up existing scripts to $backupDir"
} else {
    New-Item -ItemType Directory -Path $trackerDir -Force | Out-Null
    Write-Output "ℹ️  No existing scripts to back up."
}
```

## Step 4: Update Hook Scripts

Copy the latest hook scripts from the plugin's `shared/` directory. Same logic as initialize-machine Step 2.

```powershell
# Find plugin's shared directory
$skillDir = $PSScriptRoot
if (-not $skillDir) {
    $pluginRoot = Get-ChildItem "$env:USERPROFILE\.copilot\installed-plugins" -Directory -Recurse -Filter "copilot-session-tracker" -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "plugin.json") } |
        Select-Object -First 1
    if ($pluginRoot) {
        $sharedDir = Join-Path $pluginRoot.FullName "shared"
    }
} else {
    $pluginRootDir = Split-Path (Split-Path $skillDir)
    $sharedDir = Join-Path $pluginRootDir "shared"
}

# Fallback to repo source
if (-not $sharedDir -or -not (Test-Path $sharedDir)) {
    $repoShared = Join-Path $PWD "plugins\copilot-session-tracker\shared"
    if (Test-Path $repoShared) {
        $sharedDir = $repoShared
    } else {
        Write-Error "❌ Cannot find shared files. Run from the copilot-tracker repo or ensure the plugin is installed."
        return
    }
}

# Copy updated scripts
Copy-Item -Path (Join-Path $sharedDir "Invoke-TrackerHook.ps1") -Destination $trackerDir -Force
Copy-Item -Path (Join-Path $sharedDir "Get-TrackerToken.ps1") -Destination $trackerDir -Force

# Copy any other .ps1 files that exist in shared/ (future-proofing)
Get-ChildItem $sharedDir -Filter "*.ps1" | ForEach-Object {
    Copy-Item $_.FullName -Destination $trackerDir -Force
}

Write-Output "✅ Hook scripts updated in $trackerDir"
```

## Step 5: Regenerate Hooks Configuration

Regenerate hooks for each detected tool. This uses the same merge logic as initialize-machine Step 4. It preserves non-tracker hooks in both files.

### For Copilot CLI (if "copilot" in $installedTools)

```powershell
if ($installedTools -contains "copilot") {
    $hooksDir = Join-Path $env:USERPROFILE ".copilot\hooks"
    $hooksPath = Join-Path $hooksDir "hooks.json"
    $hookScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Invoke-TrackerHook.ps1"

    $trackerHookTypes = @(
        @{ name = "sessionStart";         timeoutSec = 15 }
        @{ name = "sessionEnd";           timeoutSec = 15 }
        @{ name = "userPromptSubmitted";  timeoutSec = 15 }
        @{ name = "agentStop";            timeoutSec = 15 }
        @{ name = "subagentStart";        timeoutSec = 15 }
        @{ name = "subagentStop";         timeoutSec = 15 }
        @{ name = "notification";         timeoutSec = 15 }
        @{ name = "postToolUse";          timeoutSec = 10 }
    )

    $trackerHooks = [ordered]@{}
    foreach ($hook in $trackerHookTypes) {
        $trackerHooks[$hook.name] = @(
            @{
                type       = "command"
                powershell = "powershell -ExecutionPolicy Bypass -File `"$hookScript`" -HookType $($hook.name)"
                comment    = "Copilot Session Tracker"
                timeoutSec = $hook.timeoutSec
            }
        )
    }

    if (-not (Test-Path $hooksDir)) {
        New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    }

    $existingHookEntries = [ordered]@{}
    if (Test-Path $hooksPath) {
        try {
            $existingHooksObj = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable
            if ($existingHooksObj.ContainsKey("hooks")) {
                $existingHookEntries = $existingHooksObj["hooks"]
            } else {
                foreach ($k in $existingHooksObj.Keys) {
                    if ($k -ne "version") { $existingHookEntries[$k] = $existingHooksObj[$k] }
                }
            }
        } catch {
            # Back up corrupted file
            $backupPath = "$hooksPath.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Copy-Item $hooksPath $backupPath -Force
            Write-Warning "⚠️  Existing hooks.json was invalid. Backed up to $backupPath. Starting fresh."
        }
    }

    foreach ($hookType in $trackerHooks.Keys) {
        $existingEntries = @()
        if ($existingHookEntries.ContainsKey($hookType)) {
            $existingEntries = @($existingHookEntries[$hookType] | Where-Object {
                $ps = if ($_.powershell) { $_.powershell } else { "" }
                $ps -notmatch "Invoke-TrackerHook"
            })
        }
        $existingHookEntries[$hookType] = @($existingEntries) + $trackerHooks[$hookType]
    }

    $finalHooks = [ordered]@{
        version = 1
        hooks   = $existingHookEntries
    }

    $tempPath = "$hooksPath.tmp"
    $finalHooks | ConvertTo-Json -Depth 10 | Set-Content -Path $tempPath -Encoding UTF8
    Move-Item -Path $tempPath -Destination $hooksPath -Force

    # Validate
    try {
        $validation = Get-Content $hooksPath -Raw | ConvertFrom-Json
        $keys = ($validation.hooks | Get-Member -MemberType NoteProperty).Name
        Write-Output "✅ Copilot CLI hooks updated: $($keys.Count) hook types in $hooksPath"
    } catch {
        Write-Error "❌ hooks.json validation failed after write!"
    }
}
```

### For Claude Code (if "claude" in $installedTools)

```powershell
if ($installedTools -contains "claude") {
    $settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
    $hookScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Invoke-TrackerHook.ps1"

    $claudeHookMappings = @(
        @{ claudeEvent = "SessionStart";      apiHookType = "sessionStart";         timeout = 15; matcher = "startup|resume" }
        @{ claudeEvent = "SessionEnd";        apiHookType = "sessionEnd";           timeout = 15; matcher = $null }
        @{ claudeEvent = "UserPromptSubmit";  apiHookType = "userPromptSubmitted";  timeout = 15; matcher = $null }
        @{ claudeEvent = "Stop";              apiHookType = "agentStop";            timeout = 15; matcher = $null }
        @{ claudeEvent = "SubagentStart";     apiHookType = "subagentStart";        timeout = 15; matcher = $null }
        @{ claudeEvent = "SubagentStop";      apiHookType = "subagentStop";         timeout = 15; matcher = $null }
        @{ claudeEvent = "Notification";      apiHookType = "notification";         timeout = 15; matcher = $null }
        @{ claudeEvent = "PostToolUse";       apiHookType = "postToolUse";          timeout = 10; matcher = $null }
    )

    $claudeHooks = [ordered]@{}
    foreach ($mapping in $claudeHookMappings) {
        $handler = [ordered]@{
            type    = "command"
            command = "powershell -ExecutionPolicy Bypass -File `"$hookScript`" -HookType $($mapping.apiHookType) -Tool claude"
            shell   = "powershell"
            async   = $true
            timeout = $mapping.timeout
        }

        $matcherGroup = [ordered]@{
            hooks = @($handler)
        }
        if ($mapping.matcher) {
            $matcherGroup.matcher = $mapping.matcher
        }

        $claudeHooks[$mapping.claudeEvent] = @($matcherGroup)
    }

    $settingsDir = Split-Path $settingsPath
    if (-not (Test-Path $settingsDir)) {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    }

    $existingSettings = @{}
    if (Test-Path $settingsPath) {
        try {
            $existingSettings = Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable
        } catch {
            $backupPath = "$settingsPath.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Copy-Item $settingsPath $backupPath -Force
            Write-Warning "⚠️  Existing settings.json was invalid. Backed up to $backupPath. Starting fresh."
            $existingSettings = @{}
        }
    }

    if (-not $existingSettings.ContainsKey("hooks")) {
        $existingSettings["hooks"] = @{}
    }

    foreach ($eventName in $claudeHooks.Keys) {
        $existingEntries = @()
        if ($existingSettings["hooks"].ContainsKey($eventName)) {
            $existingEntries = @($existingSettings["hooks"][$eventName] | Where-Object {
                $hasTracker = $false
                if ($_.hooks) {
                    foreach ($h in $_.hooks) {
                        if ($h.command -and $h.command -match "Invoke-TrackerHook") { $hasTracker = $true }
                    }
                }
                -not $hasTracker
            })
        }
        $existingSettings["hooks"][$eventName] = @($existingEntries) + $claudeHooks[$eventName]
    }

    $tempPath = "$settingsPath.tmp"
    $existingSettings | ConvertTo-Json -Depth 10 | Set-Content -Path $tempPath -Encoding UTF8
    Move-Item -Path $tempPath -Destination $settingsPath -Force

    try {
        $null = Get-Content $settingsPath -Raw | ConvertFrom-Json
        Write-Output "✅ Claude Code hooks updated: $settingsPath"
    } catch {
        Write-Error "❌ settings.json validation failed after write!"
    }
}
```

## Step 6: Update Config Version

Write the updated version to the config file without changing any other fields.

```powershell
$config["installedVersion"] = "4.0.0"
$config | ConvertTo-Json -Depth 4 | Set-Content -Path $configPath -Encoding UTF8
Write-Output "✅ Config updated: installedVersion = 4.0.0"
```

## Step 7: Verify

Run lightweight verification: server connectivity and auth (warn-only).

```powershell
$serverUrl = $config["serverUrl"]
$tenantId = $config["tenantId"]
$resourceId = $config["resourceId"]
$authMode = $config["authMode"]

# 7a. Server connectivity
try {
    $health = Invoke-RestMethod -Uri "$serverUrl/api/health" -ErrorAction Stop
    Write-Output "✅ Server reachable at $serverUrl"
} catch {
    Write-Warning "⚠️  Server connectivity check failed: $_"
    Write-Warning "   Hooks will still fire, but data won't reach the server until connectivity is restored."
}

# 7b. Auth verification (warn-only, never blocks)
if ($authMode -eq "certificate") {
    try {
        $tokenScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Get-TrackerToken.ps1"
        $tokenResult = & $tokenScript 2>&1
        if ($tokenResult) {
            Write-Output "✅ Certificate auth working."
        } else {
            Write-Warning "⚠️  Get-TrackerToken.ps1 returned empty. Certificate may need attention."
        }
    } catch {
        Write-Warning "⚠️  Certificate auth test failed: $_"
        Write-Warning "   Check that the certificate is still installed and valid."
    }
} else {
    try {
        $token = az account get-access-token --resource $resourceId --tenant $tenantId --query accessToken -o tsv 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output "✅ Azure CLI auth working."
        } else {
            Write-Warning "⚠️  Azure CLI token acquisition failed. You may need to run: az login --tenant $tenantId"
        }
    } catch {
        Write-Warning "⚠️  Auth verification skipped: Azure CLI not available."
    }
}

# 7c. Validate hooks configs
if ($installedTools -contains "copilot") {
    $hooksPath = Join-Path $env:USERPROFILE ".copilot\hooks\hooks.json"
    try {
        $v = Get-Content $hooksPath -Raw | ConvertFrom-Json
        if ($v.version -and $v.hooks) {
            Write-Output "✅ Copilot hooks.json is valid."
        } else {
            Write-Warning "⚠️  Copilot hooks.json may be missing version or hooks wrapper."
        }
    } catch {
        Write-Error "❌ Copilot hooks.json is invalid JSON!"
    }
}
if ($installedTools -contains "claude") {
    $settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
    try {
        $null = Get-Content $settingsPath -Raw | ConvertFrom-Json
        Write-Output "✅ Claude settings.json is valid."
    } catch {
        Write-Error "❌ Claude settings.json is invalid JSON!"
    }
}
```

## Step 8: Summary

```powershell
$toolList = ($installedTools | ForEach-Object {
    if ($_ -eq "claude") { "Claude Code" } else { "Copilot CLI" }
}) -join " + "

Write-Output @"

✅ Update Complete!

Previous Version: $previousVersion
Current Version:  4.0.0
Tools Updated:    $toolList
Machine:          $env:COMPUTERNAME
Auth Mode:        $($config['authMode'])
Hook Scripts:     $env:USERPROFILE\.copilot\copilot-tracker\
Server:           $($config['serverUrl'])
Script Backup:    $backupDir

Changes applied:
  - Updated Invoke-TrackerHook.ps1 and Get-TrackerToken.ps1
  - Regenerated hooks configuration for $toolList
  - Updated installedVersion in config

To verify: start a new session in $toolList and check the dashboard.
"@
```

## Important Notes

- **Non-destructive.** Existing config is preserved. Only hook scripts and hooks config are updated.
- **Backs up scripts.** Old scripts are saved to a timestamped backup directory before overwriting.
- **Backs up corrupted files.** If hooks.json or settings.json is invalid JSON, a backup is made before replacing.
- **Handles both tools.** Scans for tracker hooks in both `~/.copilot/hooks/hooks.json` and `~/.claude/settings.json`. Updates whichever are found (or both).
- **Preserves non-tracker hooks.** The merge logic keeps hooks from other tools/plugins in both files.
- **Warn-only auth.** Auth is verified but failures don't block the update.
- **Future-proof.** Step 2 checks for new config options. When a future version adds required options, the skill will detect and ask for them.
- **Requires initialize-machine first.** If no config file exists, the user is directed to run initialize-machine.
- **Idempotent.** Safe to run multiple times.
