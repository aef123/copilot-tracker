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

Ask the user: **"What is the tracker server URL?"**

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

## Step 2: Clean and Install Files

Remove any existing tracker scripts (from previous versions) and install fresh copies from the plugin.

```powershell
$trackerDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"

# Clean out any existing scripts from previous installs
if (Test-Path $trackerDir) {
    Get-ChildItem $trackerDir -Filter "*.ps1" | Remove-Item -Force
    Get-ChildItem $trackerDir -Filter "*.psm1" | Remove-Item -Force
    Write-Output "✅ Cleaned existing scripts from $trackerDir"
} else {
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

# Install fresh copies
Copy-Item -Path (Join-Path $sharedDir "CopilotTracker.psm1") -Destination $trackerDir -Force
Copy-Item -Path (Join-Path $sharedDir "Start-TrackerSession.ps1") -Destination $trackerDir -Force
Write-Output "✅ Module installed to $trackerDir"
```

## Step 3: Write Config File

Write the tracker config JSON. The PowerShell module reads this on every session startup.

```powershell
$config = @{
    serverUrl  = $serverUrl
    tenantId   = $tenantId
    resourceId = $resourceId
} | ConvertTo-Json

$configPath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker-config.json"
$config | Set-Content -Path $configPath -Encoding UTF8
Write-Output "✅ Config written: $configPath"
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
    $token = az account get-access-token --resource $resourceId --tenant $tenantId --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Output "✅ Authentication working."
    } else {
        Write-Warning "⚠️  Could not acquire auth token. Run: az login --tenant $tenantId"
        Write-Warning "   If you see a consent_required error, the Azure CLI (04b07795-8ddb-461a-bbee-02f9e1bf7b46)"
        Write-Warning "   may need to be pre-authorized in the Entra app registration."
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
- **Azure CLI pre-authorization required.** The Entra app registration must have the Azure CLI app ID (`04b07795-8ddb-461a-bbee-02f9e1bf7b46`) listed as a pre-authorized application. The deploy script (`deploy/scripts/setup-app-registration.ps1`) does this automatically. If you see a `consent_required` or `invalid_resource` error during token acquisition, this is the cause.
