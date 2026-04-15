---
name: initialize-machine
description: "Configure this machine for Copilot Session Tracker. Installs the PowerShell module, configures auth, and updates copilot-instructions.md so Copilot automatically tracks sessions."
argument-hint: "[server-url] [tenant-id]"
compatibility: "Windows only. Requires Azure CLI (az) and PowerShell 7+."
metadata:
  author: Copilot Session Tracker
  version: 2.0.0
  category: setup
---

# Initialize Machine for Copilot Session Tracker

This skill configures the current machine for Copilot Session Tracker. It installs the PowerShell module, verifies auth, and updates `copilot-instructions.md` so every future Copilot CLI session is automatically tracked.

**Platform: Windows only** (uses `$env:USERPROFILE`, Windows-style paths, PowerShell 7+).

## Step 0: Gather Parameters (MANDATORY — DO NOT SKIP)

**STOP. You MUST ask the user for the following parameters before proceeding. Do NOT guess or use defaults without explicit user confirmation.**

### 0a. Server URL

Ask the user: **"What is the tracker server URL? (Default: `https://copilot-tracker.azurewebsites.net`)"**

Wait for the user to respond. Store as `$serverUrl`.

```powershell
$serverUrl = "<user-provided-or-default>"
```

### 0b. Entra Tenant ID

Ask the user: **"What is the Entra tenant ID for authentication? (Default: `5df6d88f-0d78-491b-9617-8b43a209ba73`)"**

Wait for the user to respond. Store as `$tenantId`.

```powershell
$tenantId = "<user-provided-or-default>"
```

### 0c. App Registration Resource ID

Ask the user: **"What is the Entra app registration resource ID (Application ID URI)? (Default: `api://4c8148f5-c913-40c5-863f-1c019821eac4`)"**

Wait for the user to respond. Store as `$resourceId`.

```powershell
$resourceId = "<user-provided-or-default>"
```

### Confirmation Gate

Display the collected parameters and ask: **"I'll configure the tracker with these settings. Proceed?"**

```
Server URL:  <server-url>
Tenant ID:   <tenant-id>
Resource ID: <resource-id>
```

**Do NOT proceed until the user explicitly confirms.**

## Step 1: Verify Prerequisites

```powershell
# 1a. Check Azure CLI
try {
    az version | Out-Null
    Write-Output "✅ Azure CLI is installed."
} catch {
    Write-Error "❌ Azure CLI (az) is not installed. Install from https://aka.ms/install-azure-cli"
    return
}

# 1b. Check login state
$account = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Not logged in to Azure CLI. Run 'az login' first."
    return
}
Write-Output "✅ Logged in to Azure CLI."

# 1c. Test token acquisition for the tracker tenant
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

# 1d. Verify server is reachable
try {
    $health = Invoke-RestMethod -Uri "$serverUrl/api/health" -ErrorAction Stop
    Write-Output "✅ Server reachable at $serverUrl (active sessions: $($health.activeSessions))"
} catch {
    Write-Error "❌ Cannot reach server at $serverUrl. Check the URL."
    return
}
```

## Step 2: Create Directory and Install Files

Find the module files relative to this plugin's installed location, then copy them to the user's machine.

```powershell
$trackerDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"
if (-not (Test-Path $trackerDir)) {
    New-Item -ItemType Directory -Path $trackerDir -Force | Out-Null
}

# Find files relative to this plugin's install location
# The plugin structure is: <plugin-root>/skills/initialize-machine/SKILL.md
# Shared files are at:     <plugin-root>/shared/
$skillDir = $PSScriptRoot
if (-not $skillDir) {
    # Fallback: search for the installed plugin by name
    $pluginRoot = Get-ChildItem "$env:USERPROFILE\.copilot\installed-plugins" -Directory -Recurse -Filter "copilot-session-tracker" -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "plugin.json") } |
        Select-Object -First 1
    if ($pluginRoot) {
        $sharedDir = Join-Path $pluginRoot.FullName "shared"
        $templateDir = Join-Path $pluginRoot.FullName "templates"
    }
} else {
    # Resolve from SKILL.md location: ../../shared/
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
        Write-Error "❌ Cannot find module files. Run from the copilot-tracker repo or ensure the plugin is installed."
        return
    }
}

# Copy module files
Copy-Item -Path (Join-Path $sharedDir "CopilotTracker.psm1") -Destination $trackerDir -Force
Copy-Item -Path (Join-Path $sharedDir "Start-TrackerSession.ps1") -Destination $trackerDir -Force
Write-Output "✅ Module installed to $trackerDir"
```

## Step 3: Configure Environment Variables

Set or clear environment variables based on whether the user chose non-default values.

```powershell
# Server URL
if ($serverUrl -ne "https://copilot-tracker.azurewebsites.net") {
    [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_URL", $serverUrl, "User")
    $env:COPILOT_TRACKER_URL = $serverUrl
    Write-Output "✅ Set COPILOT_TRACKER_URL = $serverUrl"
} else {
    # Clear any previously set custom value
    if ([System.Environment]::GetEnvironmentVariable("COPILOT_TRACKER_URL", "User")) {
        [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_URL", $null, "User")
        $env:COPILOT_TRACKER_URL = $null
        Write-Output "✅ Cleared COPILOT_TRACKER_URL (using default)"
    }
}

# Tenant ID
if ($tenantId -ne "5df6d88f-0d78-491b-9617-8b43a209ba73") {
    [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_TENANT_ID", $tenantId, "User")
    $env:COPILOT_TRACKER_TENANT_ID = $tenantId
    Write-Output "✅ Set COPILOT_TRACKER_TENANT_ID = $tenantId"
} else {
    if ([System.Environment]::GetEnvironmentVariable("COPILOT_TRACKER_TENANT_ID", "User")) {
        [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_TENANT_ID", $null, "User")
        $env:COPILOT_TRACKER_TENANT_ID = $null
        Write-Output "✅ Cleared COPILOT_TRACKER_TENANT_ID (using default)"
    }
}

# Resource ID
if ($resourceId -ne "api://4c8148f5-c913-40c5-863f-1c019821eac4") {
    [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_RESOURCE_ID", $resourceId, "User")
    $env:COPILOT_TRACKER_RESOURCE_ID = $resourceId
    Write-Output "✅ Set COPILOT_TRACKER_RESOURCE_ID = $resourceId"
} else {
    if ([System.Environment]::GetEnvironmentVariable("COPILOT_TRACKER_RESOURCE_ID", "User")) {
        [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_RESOURCE_ID", $null, "User")
        $env:COPILOT_TRACKER_RESOURCE_ID = $null
        Write-Output "✅ Cleared COPILOT_TRACKER_RESOURCE_ID (using default)"
    }
}
```

## Step 4: Update copilot-instructions.md

Read the template and inject into the user's `copilot-instructions.md`. This is idempotent: running again replaces the existing section.

```powershell
$templatePath = Join-Path $templateDir "copilot-instructions-snippet.md"
if (-not (Test-Path $templatePath)) {
    Write-Error "❌ Cannot find copilot-instructions-snippet.md template."
    return
}

$snippet = Get-Content $templatePath -Raw
$instructionsPath = Join-Path $env:USERPROFILE ".copilot\copilot-instructions.md"
$beginMarker = "<!-- BEGIN COPILOT SESSION TRACKER -->"
$endMarker = "<!-- END COPILOT SESSION TRACKER -->"

if (Test-Path $instructionsPath) {
    $existing = Get-Content $instructionsPath -Raw

    if ($existing -match [regex]::Escape($beginMarker)) {
        $pattern = "(?s)$([regex]::Escape($beginMarker)).*?$([regex]::Escape($endMarker))"
        $existingSection = [regex]::Match($existing, $pattern).Value

        if ($existingSection.Trim() -eq $snippet.Trim()) {
            Write-Output "✅ copilot-instructions.md already up to date."
        } else {
            $updated = $existing -replace $pattern, $snippet
            $updated | Set-Content -Path $instructionsPath -Encoding UTF8
            Write-Output "✅ Updated tracker instructions in copilot-instructions.md"
        }
    } else {
        "`n`n$snippet" | Add-Content -Path $instructionsPath -Encoding UTF8
        Write-Output "✅ Appended tracker instructions to copilot-instructions.md"
    }
} else {
    $snippet | Set-Content -Path $instructionsPath -Encoding UTF8
    Write-Output "✅ Created copilot-instructions.md with tracker instructions."
}
```

## Step 5: Verify Setup

Test the full chain without creating real sessions.

```powershell
$modulePath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\CopilotTracker.psm1"

try {
    Import-Module $modulePath -Force
    Write-Output "✅ Module loaded successfully."
} catch {
    Write-Error "❌ Failed to load module: $_"
    return
}

# Verify auth by testing token acquisition (non-destructive)
try {
    Initialize-TrackerConnection
    $headers = Get-TrackerHeaders
    if ($headers) {
        Write-Output "✅ Authentication working."
    } else {
        Write-Warning "⚠️  Could not acquire auth token. Run: az login --tenant $tenantId"
    }
} catch {
    Write-Warning "⚠️  Auth verification failed: $_"
}

# Verify server connectivity (non-destructive, uses anonymous health endpoint)
try {
    $health = Invoke-RestMethod -Uri "$serverUrl/api/health" -ErrorAction Stop
    Write-Output "✅ Server connectivity verified."
} catch {
    Write-Warning "⚠️  Server connectivity check failed: $_"
}
```

## Step 6: Output Summary

```powershell
Write-Output @"

✅ Machine Initialized!

Machine:          $env:COMPUTERNAME
Module installed: $env:USERPROFILE\.copilot\copilot-tracker\
Instructions:     $env:USERPROFILE\.copilot\copilot-instructions.md (updated)
Server:           $serverUrl
Tenant:           $tenantId
Resource ID:      $resourceId

Copilot CLI will now automatically track sessions on this machine.
To test: start a new Copilot CLI session and check the dashboard.
"@
```

## Important Notes

- **Windows only.** All paths use `$env:USERPROFILE` and Windows-style separators.
- **Don't create new modules.** Always copy from the plugin's `shared/` directory.
- **Don't blindly paste instructions.** The `BEGIN/END` markers ensure idempotent updates.
- **Idempotent.** Running multiple times is safe.
- **Multi-tenant friendly.** Uses `--tenant` on `az account get-access-token` so your active subscription doesn't matter.
- **Non-destructive verification.** Uses the anonymous `/api/health` endpoint, no test sessions created.
