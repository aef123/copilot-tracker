---
name: initialize-machine
description: "Configure this machine for AI Session Tracker. Sets up hooks-based tracking for Claude Code or Copilot CLI with certificate or user authentication."
argument-hint: "[server-url] [tenant-id]"
compatibility: "Windows only. Requires PowerShell 7+. Azure CLI required for user auth mode."
metadata:
  author: Copilot Session Tracker
  version: 4.0.0
  category: setup
---

# Initialize Machine for AI Session Tracker

This skill configures the current machine for hooks-based AI Session Tracker. It installs hook scripts, configures authentication (certificate or Azure CLI), generates hooks configuration for either Claude Code or Copilot CLI, and verifies connectivity.

**Platform: Windows only** (uses `$env:USERPROFILE`, Windows-style paths, PowerShell 7+).

## Step 0: Gather Base Parameters (MANDATORY — DO NOT SKIP)

**STOP. You MUST ask the user for the following parameters before proceeding. Do NOT guess or use defaults without explicit user confirmation.**

### 0a. Server URL

Ask the user: **"What is the tracker server URL?"** (e.g., `https://copilot-tracker.azurewebsites.net`)

This is required. There is no default. Store as `$serverUrl`.

```powershell
$serverUrl = "<user-provided>"
```

### 0b. Entra Tenant ID

Ask the user: **"What is the Entra tenant ID for authentication?"**

This is required. There is no default. Store as `$tenantId`.

```powershell
$tenantId = "<user-provided>"
```

### 0c. App Registration Resource ID

Ask the user: **"What is the Entra app registration resource ID (Application ID URI)? This looks like `api://<client-id>`."**

This is required. There is no default. Store as `$resourceId`.

```powershell
$resourceId = "<user-provided>"
```

### 0d. Detect Tool

Determine if this is being run from Claude Code or GitHub Copilot CLI.

**Detection method:** Check for the `CLAUDE_PROJECT_DIR` environment variable. If it exists, you are running in Claude Code. Otherwise, check if `~/.copilot/hooks/` exists (indicating Copilot CLI).

```powershell
if ($env:CLAUDE_PROJECT_DIR) {
    $detectedTool = "claude"
    Write-Output "🔍 Detected: Claude Code"
} else {
    $detectedTool = "copilot"
    Write-Output "🔍 Detected: GitHub Copilot CLI"
}
```

Ask the user to confirm: **"I detected you're running [tool]. Is this correct? (yes/no)"**

If the user says no, ask them which tool they want to configure for.

Store as `$tool`.

## Step 1: Ask About Authentication Mode

Ask the user: **"Do you want to authenticate using an app registration and certificate? (yes/no)"**

- If **YES**: proceed to Step 1a (Certificate Auth).
- If **NO**: proceed to Step 1b (User Auth).

### Step 1a: Certificate Authentication

Ask the user for:
- **Certificate Subject Name** (e.g., `CN=client.copilottracker.andrewfaust.com`)
- **App Client ID** (e.g., `64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e`)

```powershell
$certSubject = "<user-provided>"
$clientId = "<user-provided>"
$authMode = "certificate"

# Verify certificate exists in the user's certificate store
$certs = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject }
if (-not $certs -or $certs.Count -eq 0) {
    Write-Error "❌ No certificate found in Cert:\CurrentUser\My with subject '$certSubject'."
    Write-Error "   Install the certificate first, then re-run this skill."
    return
}

# If multiple certs match, pick the newest non-expired one
$cert = $certs | Where-Object { $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    Write-Error "❌ Certificate found but either expired or missing private key."
    Write-Error "   Subject: $certSubject"
    Write-Error "   Matches: $($certs.Count), but none are valid (not expired + has private key)."
    return
}

$certThumbprint = $cert.Thumbprint
Write-Output "✅ Certificate verified:"
Write-Output "   Subject:    $($cert.Subject)"
Write-Output "   Thumbprint: $certThumbprint"
Write-Output "   Expires:    $($cert.NotAfter)"
Write-Output "   Private Key: Yes"

# Test token acquisition using the certificate
try {
    Add-Type -Path (Join-Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) "System.IdentityModel.Tokens.Jwt.dll") -ErrorAction SilentlyContinue
} catch {}

# Use MSAL.PS or direct MSAL to test certificate auth
# The Get-TrackerToken.ps1 script handles this at runtime, so we do a lightweight test here
Write-Output "✅ Certificate auth mode configured. Token acquisition will be tested in Step 5."
```

### Step 1b: User Authentication (Azure CLI)

```powershell
$authMode = "user"

# Check Azure CLI
try {
    az version | Out-Null
    Write-Output "✅ Azure CLI is installed."
} catch {
    Write-Error "❌ Azure CLI (az) is not installed. Install from https://aka.ms/install-azure-cli"
    return
}

# Check login state
$account = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Not logged in to Azure CLI. Run 'az login' first."
    return
}
Write-Output "✅ Logged in to Azure CLI."

# Test token acquisition
$token = az account get-access-token --resource $resourceId --tenant $tenantId --query accessToken -o tsv 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warning "⚠️  Cannot get a token for tenant $tenantId. Attempting login..."
    az login --tenant $tenantId
    $token = az account get-access-token --resource $resourceId --tenant $tenantId --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Still cannot get a token after login attempt."
        return
    }
}
Write-Output "✅ Token acquired for tenant $tenantId"
```

### Confirmation Gate

Display the collected parameters and ask: **"I'll configure the tracker with these settings. Proceed?"**

For certificate mode:
```
Server URL:    <server-url>
Tenant ID:     <tenant-id>
Resource ID:   <resource-id>
Auth Mode:     certificate
Client ID:     <client-id>
Cert Subject:  <cert-subject>
Cert Thumbprint: <thumbprint>
```

For user mode:
```
Server URL:    <server-url>
Tenant ID:     <tenant-id>
Resource ID:   <resource-id>
Auth Mode:     user (Azure CLI)
```

**Do NOT proceed until the user explicitly confirms.**

## Step 2: Install Hook Scripts

Copy the hook scripts from the plugin's `shared/` directory to the user's machine. These scripts are shared between both Claude Code and Copilot CLI and always install to `~/.copilot/copilot-tracker/`.

```powershell
$trackerDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"

# Create directory if it doesn't exist
if (-not (Test-Path $trackerDir)) {
    New-Item -ItemType Directory -Path $trackerDir -Force | Out-Null
}

# Find plugin's shared directory
$skillDir = $PSScriptRoot
if (-not $skillDir) {
    $pluginRoot = Get-ChildItem "$env:USERPROFILE\.copilot\installed-plugins" -Directory -Recurse -Filter "copilot-session-tracker" -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "plugin.json") } |
        Select-Object -First 1
    if ($pluginRoot) {
        $sharedDir = Join-Path $pluginRoot.FullName "shared"
        $templateDir = Join-Path $pluginRoot.FullName "templates"
    }
} else {
    $pluginRootDir = Split-Path (Split-Path $skillDir)
    $sharedDir = Join-Path $pluginRootDir "shared"
    $templateDir = Join-Path $pluginRootDir "templates"
}

# Fallback to repo source if running from repo directory
if (-not $sharedDir -or -not (Test-Path $sharedDir)) {
    $repoShared = Join-Path $PWD "plugins\copilot-session-tracker\shared"
    $repoTemplates = Join-Path $PWD "plugins\copilot-session-tracker\templates"
    if (Test-Path $repoShared) {
        $sharedDir = $repoShared
        $templateDir = $repoTemplates
    } else {
        Write-Error "❌ Cannot find shared files. Run from the copilot-tracker repo or ensure the plugin is installed."
        return
    }
}

# Copy hook scripts
Copy-Item -Path (Join-Path $sharedDir "Invoke-TrackerHook.ps1") -Destination $trackerDir -Force
Copy-Item -Path (Join-Path $sharedDir "Get-TrackerToken.ps1") -Destination $trackerDir -Force
Write-Output "✅ Hook scripts installed to $trackerDir"
```

## Step 3: Write Config File

Write the tracker config JSON at `~/.copilot/copilot-tracker-config.json`.

```powershell
$configObj = @{
    serverUrl        = $serverUrl
    tenantId         = $tenantId
    resourceId       = $resourceId
    authMode         = $authMode
    installedVersion = "4.0.0"
}

# Add certificate-specific fields only for certificate auth
if ($authMode -eq "certificate") {
    $configObj.clientId = $clientId
    $configObj.certificateSubject = $certSubject
    $configObj.certificateThumbprint = $certThumbprint
}

$config = $configObj | ConvertTo-Json -Depth 4
$configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"
$config | Set-Content -Path $configPath -Encoding UTF8
Write-Output "✅ Config written: $configPath"
```

## Step 4: Generate Hooks Configuration

Create or merge hooks configuration for the detected tool. For Copilot CLI, this generates `~/.copilot/hooks/hooks.json`. For Claude Code, this merges into `~/.claude/settings.json`.

### Claude Code Hooks (`$tool -eq "claude"`)

**CRITICAL FORMAT:** Claude Code hooks use PascalCase event names, `type: "command"` with a `command` field (not `powershell`), `shell: "powershell"` on Windows, `timeout` (not `timeoutSec`), and `async: true` for fire-and-forget execution. No `version` or `comment` fields.

```powershell
if ($tool -eq "claude") {
    # Claude Code hooks go in ~/.claude/settings.json
    $settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
    $hookScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Invoke-TrackerHook.ps1"
    
    # Define Claude hook mappings: Claude event name -> API hook type
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
    
    # Build Claude hooks structure
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
    
    # Read existing settings.json and merge
    $settingsDir = Split-Path $settingsPath
    if (-not (Test-Path $settingsDir)) {
        New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    }
    
    $existingSettings = @{}
    if (Test-Path $settingsPath) {
        try {
            $existingSettings = Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable
            Write-Output "✅ Read existing settings.json"
        } catch {
            Write-Warning "⚠️  Existing settings.json was invalid, starting fresh."
            $existingSettings = @{}
        }
    }
    
    # Merge hooks: preserve non-tracker hooks in each event
    if (-not $existingSettings.ContainsKey("hooks")) {
        $existingSettings["hooks"] = @{}
    }
    
    foreach ($eventName in $claudeHooks.Keys) {
        $existingEntries = @()
        if ($existingSettings["hooks"].ContainsKey($eventName)) {
            # Keep entries whose command does not reference Invoke-TrackerHook
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
    
    # Write atomically
    $tempPath = "$settingsPath.tmp"
    $existingSettings | ConvertTo-Json -Depth 10 | Set-Content -Path $tempPath -Encoding UTF8
    Move-Item -Path $tempPath -Destination $settingsPath -Force
    Write-Output "✅ Claude Code hooks written: $settingsPath"
}
```

### Copilot CLI Hooks (`$tool -eq "copilot"`)

**CRITICAL FORMAT:** hooks.json MUST have `"version": 1` at the top level and all hook types MUST be inside a `"hooks"` wrapper object. Each entry uses `"type": "command"` and a `"powershell"` string (not `command`/`args`). Getting this wrong causes Copilot CLI to silently ignore all hooks.

```powershell
else {
    $hooksDir = Join-Path $env:USERPROFILE ".copilot\hooks"
    $hooksPath = Join-Path $hooksDir "hooks.json"
    $hookScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Invoke-TrackerHook.ps1"

    # Define the tracker hooks
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

    # Build tracker hook entries using the CORRECT format:
    # { "type": "command", "powershell": "<full command string>", "timeoutSec": N }
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

    # Read existing hooks.json if it exists, preserving non-tracker entries
    if (-not (Test-Path $hooksDir)) {
        New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    }

    $existingHooksObj = $null
    $existingHookEntries = [ordered]@{}
    if (Test-Path $hooksPath) {
        try {
            $existingHooksObj = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable
            # Extract the hooks from inside the "hooks" wrapper (if present)
            if ($existingHooksObj.ContainsKey("hooks")) {
                $existingHookEntries = $existingHooksObj["hooks"]
            } else {
                # Legacy format without wrapper - treat all keys except "version" as hooks
                foreach ($k in $existingHooksObj.Keys) {
                    if ($k -ne "version") { $existingHookEntries[$k] = $existingHooksObj[$k] }
                }
            }
            Write-Output "✅ Read existing hooks.json"
        } catch {
            Write-Warning "⚠️  Existing hooks.json was invalid, starting fresh."
        }
    }

    # Merge: for each tracker hook type, keep non-tracker entries and add tracker entry
    foreach ($hookType in $trackerHooks.Keys) {
        $existingEntries = @()
        if ($existingHookEntries.ContainsKey($hookType)) {
            # Keep entries whose powershell field does not reference Invoke-TrackerHook
            $existingEntries = @($existingHookEntries[$hookType] | Where-Object {
                $ps = if ($_.powershell) { $_.powershell } else { "" }
                $ps -notmatch "Invoke-TrackerHook"
            })
        }
        $existingHookEntries[$hookType] = @($existingEntries) + $trackerHooks[$hookType]
    }

    # Build final structure with version and hooks wrapper
    $finalHooks = [ordered]@{
        version = 1
        hooks   = $existingHookEntries
    }

    # Write atomically: write to temp file then move
    $tempPath = "$hooksPath.tmp"
    $finalHooks | ConvertTo-Json -Depth 10 | Set-Content -Path $tempPath -Encoding UTF8
    Move-Item -Path $tempPath -Destination $hooksPath -Force
    Write-Output "✅ hooks.json written: $hooksPath"

    # Validate the result
    try {
        $validation = Get-Content $hooksPath -Raw | ConvertFrom-Json
        if (-not $validation.version) {
            Write-Warning "⚠️  hooks.json is missing 'version' field!"
        }
        if (-not $validation.hooks) {
            Write-Warning "⚠️  hooks.json is missing 'hooks' wrapper!"
        }
        $keys = ($validation.hooks | Get-Member -MemberType NoteProperty).Name
        $badKeys = $keys | Where-Object { $_ -cmatch "^[A-Z]" }
        if ($badKeys) {
            Write-Warning "⚠️  Found non-camelCase keys in hooks.json: $($badKeys -join ', ')"
        } else {
            Write-Output "✅ hooks.json validated: version=$($validation.version), $($keys.Count) hook types, all camelCase."
        }
    } catch {
        Write-Error "❌ hooks.json is not valid JSON after write!"
        return
    }
}
```

## Step 5: Verify Setup

Test token acquisition, server connectivity, and hooks.json validity.

```powershell
# 5a. Test token acquisition
if ($authMode -eq "certificate") {
    # Test certificate-based token acquisition via Get-TrackerToken.ps1
    try {
        $tokenScript = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\Get-TrackerToken.ps1"
        $tokenResult = & $tokenScript 2>&1
        if ($tokenResult) {
            Write-Output "✅ Certificate token acquisition working."
        } else {
            Write-Warning "⚠️  Get-TrackerToken.ps1 returned empty. Check certificate configuration."
        }
    } catch {
        Write-Warning "⚠️  Certificate token test failed: $_"
        Write-Warning "   Verify the certificate is installed and the app registration is configured correctly."
    }
} else {
    # Test Azure CLI token acquisition
    try {
        $token = az account get-access-token --resource $resourceId --tenant $tenantId --query accessToken -o tsv 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output "✅ Azure CLI token acquisition working."
        } else {
            Write-Warning "⚠️  Could not acquire token. Run: az login --tenant $tenantId"
        }
    } catch {
        Write-Warning "⚠️  Auth verification failed: $_"
    }
}

# 5b. Verify server connectivity
try {
    $health = Invoke-RestMethod -Uri "$serverUrl/api/health" -ErrorAction Stop
    Write-Output "✅ Server reachable at $serverUrl"
} catch {
    Write-Warning "⚠️  Server connectivity check failed: $_"
}

# 5c. Confirm hooks configuration is valid
if ($tool -eq "claude") {
    $settingsPath = Join-Path $env:USERPROFILE ".claude\settings.json"
    try {
        $null = Get-Content $settingsPath -Raw | ConvertFrom-Json
        Write-Output "✅ settings.json is valid JSON."
    } catch {
        Write-Error "❌ settings.json validation failed: $_"
    }
} else {
    try {
        $null = Get-Content $hooksPath -Raw | ConvertFrom-Json
        Write-Output "✅ hooks.json is valid JSON."
    } catch {
        Write-Error "❌ hooks.json validation failed: $_"
    }
}
```

## Step 6: Update copilot-instructions.md

If a `copilot-instructions.md` exists, remove the old Copilot Session Tracker section entirely. Hooks handle everything automatically, so no instructions section is needed.

```powershell
$instructionsPath = Join-Path $env:USERPROFILE ".copilot\copilot-instructions.md"
$beginMarker = "<!-- BEGIN COPILOT SESSION TRACKER -->"
$endMarker = "<!-- END COPILOT SESSION TRACKER -->"

if (Test-Path $instructionsPath) {
    $existing = Get-Content $instructionsPath -Raw

    if ($existing -match [regex]::Escape($beginMarker)) {
        # Remove the entire old tracker section (including markers and surrounding blank lines)
        $pattern = "(?s)\r?\n*$([regex]::Escape($beginMarker)).*?$([regex]::Escape($endMarker))\r?\n*"
        $updated = $existing -replace $pattern, "`n"
        $updated = $updated.TrimEnd() + "`n"
        $updated | Set-Content -Path $instructionsPath -Encoding UTF8
        Write-Output "✅ Removed old tracker instructions from copilot-instructions.md"
        Write-Output "   (Hooks handle tracking automatically — no instructions section needed.)"
    } else {
        Write-Output "✅ No tracker section found in copilot-instructions.md. Nothing to remove."
    }
} else {
    Write-Output "ℹ️  No copilot-instructions.md found. Skipping."
}
```

## Step 7: Output Summary

```powershell
$summaryAuth = if ($authMode -eq "certificate") {
    "certificate ($certSubject)"
} else {
    "user (Azure CLI)"
}

$summaryTool = if ($tool -eq "claude") { "Claude Code" } else { "Copilot CLI" }
$summaryHooksConfig = if ($tool -eq "claude") {
    "$env:USERPROFILE\.claude\settings.json"
} else {
    "$env:USERPROFILE\.copilot\hooks\hooks.json"
}

Write-Output @"

✅ Machine Initialized!

Tool:          $summaryTool
Machine:       $env:COMPUTERNAME
Auth Mode:     $summaryAuth
Hook Scripts:  $env:USERPROFILE\.copilot\copilot-tracker\
Hooks Config:  $summaryHooksConfig
Server:        $serverUrl
Tenant:        $tenantId
Resource ID:   $resourceId

Session tracking is now fully automatic via $summaryTool hooks.
No manual tracking commands or copilot-instructions.md changes needed.
To test: start a new $summaryTool session and check the dashboard.
"@
```

## Important Notes

- **Windows only.** All paths use `$env:USERPROFILE` and Windows-style separators.
- **Don't create new scripts.** Always copy from the plugin's `shared/` directory.
- **Idempotent.** Running multiple times is safe. Both hooks.json and settings.json merges preserve non-tracker entries.
- **Two tools supported.** Claude Code hooks go to `~/.claude/settings.json` (PascalCase events, `command` field, `async: true`). Copilot CLI hooks go to `~/.copilot/hooks/hooks.json` (camelCase events, `powershell` field, `version: 1`).
- **Shared hook scripts.** Both tools use the same `~/.copilot/copilot-tracker/` directory for hook scripts. The `-Tool claude` parameter tells `Invoke-TrackerHook.ps1` which tool triggered the hook.
- **Claude Code async hooks.** All Claude hooks use `async: true` for fire-and-forget execution so tracking never blocks the user's workflow.
- **Two auth modes.** Certificate auth is preferred for automation and CI scenarios. User auth via Azure CLI is simpler for individual developers.
- **Certificate thumbprint stored.** The config stores the thumbprint (not just subject) for unambiguous certificate lookup at runtime.
- **Atomic writes.** Both hooks.json and settings.json are written to a temp file first, then moved into place.
- **No copilot-instructions.md changes needed.** Hooks handle all tracking automatically. The old tracker section is removed if present.
- **Multi-tenant friendly.** Uses `--tenant` on `az account get-access-token` so your active subscription doesn't matter.
- **Non-destructive verification.** Uses the anonymous `/api/health` endpoint, no test sessions created.
