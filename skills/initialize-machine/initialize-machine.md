---
name: initialize-machine
description: "Configure this machine for Copilot Session Tracker. Installs the PowerShell module, configures auth, and updates copilot-instructions.md so Copilot automatically tracks sessions."
argument-hint: "[server-url] [tenant-id]"
compatibility: "Requires Azure CLI (az) installed and authenticated. PowerShell 7+."
metadata:
  author: Copilot Session Tracker
  version: 2.0.0
  category: setup
---

# Initialize Machine for Copilot Session Tracker

This skill configures the current machine to participate in Copilot Session Tracker. It installs the PowerShell module, verifies auth, and updates `copilot-instructions.md` so every future Copilot CLI session is automatically tracked.

## Step 0: Gather Parameters

Collect the following from the user. Both have sensible defaults.

1. **Server URL** — the tracker server (default: `https://copilot-tracker.azurewebsites.net`)
2. **Tenant ID** — the Entra tenant for auth (default: `5df6d88f-0d78-491b-9617-8b43a209ba73`)

```powershell
$serverUrl = "<user-provided-or-default>"
$tenantId = "<user-provided-or-default>"
```

## Step 1: Verify Prerequisites

```powershell
# 1a. Check Azure CLI is available
try {
    az version | Out-Null
    Write-Output "✅ Azure CLI is installed."
} catch {
    Write-Error "❌ Azure CLI (az) is not installed or not on PATH. Install it from https://aka.ms/install-azure-cli"
    return
}

# 1b. Check the user is logged in
$account = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ You are not logged in to Azure CLI. Run 'az login' first."
    return
}
Write-Output "✅ Logged in to Azure CLI."

# 1c. Verify token acquisition for the tracker tenant
$token = az account get-access-token --resource "api://4c8148f5-c913-40c5-863f-1c019821eac4" --tenant $tenantId --query accessToken -o tsv 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warning "⚠️  Cannot get a token for tenant $tenantId. Attempting login..."
    az login --tenant $tenantId
    $token = az account get-access-token --resource "api://4c8148f5-c913-40c5-863f-1c019821eac4" --tenant $tenantId --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error "❌ Still cannot get a token for tenant $tenantId after login attempt."
        return
    }
}
Write-Output "✅ Token acquired for tenant $tenantId"

# 1d. Verify server is reachable
try {
    $health = Invoke-RestMethod -Uri "$serverUrl/api/health" -ErrorAction Stop
    Write-Output "✅ Server is reachable at $serverUrl"
} catch {
    Write-Error "❌ Cannot reach tracker server at $serverUrl. Check the URL and that the server is running."
    return
}
```

## Step 2: Create Directory Structure

```powershell
$trackerDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"
if (-not (Test-Path $trackerDir)) {
    New-Item -ItemType Directory -Path $trackerDir -Force | Out-Null
    Write-Output "✅ Created directory: $trackerDir"
} else {
    Write-Output "✅ Directory already exists: $trackerDir"
}
```

## Step 3: Install PowerShell Module and Startup Script

Copy `CopilotTracker.psm1` and `Start-TrackerSession.ps1` from this plugin's `shared/` directory. **Do NOT create new files or generate module code.** The source files are bundled with this plugin.

```powershell
# Find the plugin's installed location by searching for CopilotTracker.psm1
$pluginShared = Get-ChildItem "$env:USERPROFILE\.copilot\installed-plugins" -Recurse -Filter "CopilotTracker.psm1" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pluginShared) {
    # Fall back to the repo's skills/shared directory if running from source
    $repoShared = Join-Path $PWD "skills\shared\CopilotTracker.psm1"
    if (Test-Path $repoShared) {
        $pluginSharedDir = Split-Path $repoShared
    } else {
        Write-Error "❌ Cannot find CopilotTracker.psm1. Run this from the copilot-tracker repo or ensure the plugin is installed."
        return
    }
} else {
    $pluginSharedDir = $pluginShared.DirectoryName
}

$targetDir = Join-Path $env:USERPROFILE ".copilot\copilot-tracker"

Copy-Item -Path (Join-Path $pluginSharedDir "CopilotTracker.psm1") -Destination $targetDir -Force
Copy-Item -Path (Join-Path $pluginSharedDir "Start-TrackerSession.ps1") -Destination $targetDir -Force
Write-Output "✅ Module installed to $targetDir"
```

## Step 4: Set Environment Variables (if non-default)

If the user provided a custom server URL or tenant ID, persist them so the module picks them up automatically.

```powershell
if ($serverUrl -ne "https://copilot-tracker.azurewebsites.net") {
    [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_URL", $serverUrl, "User")
    Write-Output "✅ Set COPILOT_TRACKER_URL = $serverUrl (user-level env var)"
}

if ($tenantId -ne "5df6d88f-0d78-491b-9617-8b43a209ba73") {
    [System.Environment]::SetEnvironmentVariable("COPILOT_TRACKER_TENANT_ID", $tenantId, "User")
    Write-Output "✅ Set COPILOT_TRACKER_TENANT_ID = $tenantId (user-level env var)"
}
```

## Step 5: Update copilot-instructions.md

Read the instructions template, and update the user's `copilot-instructions.md`. **Do NOT blindly overwrite existing instructions.** Check whether the tracker section already exists, and if so, compare before updating.

```powershell
# Find the template
$templateLocations = @(
    (Join-Path (Split-Path $pluginSharedDir) "templates\copilot-instructions-snippet.md"),
    (Join-Path $PWD "templates\copilot-instructions-snippet.md")
)
$sourceTemplate = $templateLocations | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $sourceTemplate) {
    Write-Error "❌ Cannot find copilot-instructions-snippet.md template."
    return
}

$snippet = Get-Content $sourceTemplate -Raw
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
            Write-Output "⚠️  Tracker instructions differ from template. Updating..."
            $updated = $existing -replace $pattern, $snippet
            $updated | Set-Content -Path $instructionsPath -Encoding UTF8
            Write-Output "✅ Updated tracker instructions to latest version."
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

## Step 6: Verify Setup

```powershell
$modulePath = Join-Path $env:USERPROFILE ".copilot\copilot-tracker\CopilotTracker.psm1"

try {
    Import-Module $modulePath -Force
    Write-Output "✅ Module loaded successfully."
} catch {
    Write-Error "❌ Failed to load module: $_"
    return
}

# Test connection
try {
    Initialize-TrackerConnection
    $testSession = Start-TrackerSession -WorkingDirectory $PWD
    if ($testSession) {
        Write-Output "✅ Test session created: $testSession"
        Complete-TrackerSession -Summary "Initialize-machine verification"
        Write-Output "✅ Test session completed successfully."
    } else {
        Write-Warning "⚠️  Session creation returned null. Check server logs."
    }
} catch {
    Write-Error "❌ Connectivity test failed: $_"
    Write-Error "   Check that you have access to the server and tenant."
    return
}
```

## Step 7: Output Summary

```powershell
Write-Output @"

✅ Machine Initialized!

Machine:          $env:COMPUTERNAME
Module installed: $env:USERPROFILE\.copilot\copilot-tracker\
Instructions:     $env:USERPROFILE\.copilot\copilot-instructions.md (updated)
Server:           $serverUrl
Tenant:           $tenantId

Copilot CLI will now automatically track sessions on this machine.
"@
```

## Important Notes

- **Don't create new modules.** Always copy from the plugin's `skills/shared/` directory or the repo's `skills/shared/`.
- **Don't blindly paste instructions.** If the tracker section already exists, compare before overwriting.
- **Preserves existing content.** Only touches content between the `<!-- BEGIN -->` and `<!-- END -->` markers.
- **Idempotent.** Running multiple times is safe.
- **Multi-tenant friendly.** The `--tenant` flag on `az account get-access-token` means users can have a different active Azure subscription without breaking tracking.
- **Environment variables are optional.** Only set when using non-default values.
