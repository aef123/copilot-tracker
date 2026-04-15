# Copilot Tracker Skills

PowerShell module and startup scripts for integrating Copilot CLI with the Copilot Session Tracker.

## Setup

The module talks to the Copilot Tracker MCP server. By default it connects to:
`https://copilot-tracker.azurewebsites.net`

Override with the `-McpUrl` parameter or by calling `Initialize-TrackerConnection`.

## Prerequisites

- Azure CLI (`az login` for authentication)
- PowerShell 7+

## Files

- `shared/CopilotTracker.psm1` - Main PowerShell module
- `shared/Start-TrackerSession.ps1` - Session startup script (called from copilot-instructions.md)

## Usage

The module is automatically loaded by the Copilot CLI via the instructions in `.github/copilot-instructions.md`.
Manual usage:

```powershell
Import-Module ./skills/shared/CopilotTracker.psm1
Initialize-TrackerConnection -McpUrl "http://localhost:5000"
Start-TrackerSession -Repo "https://github.com/me/myrepo" -Branch "main"
Set-TrackerTask -Title "Building feature" -Status "started"
Complete-TrackerSession -Summary "Done"
```
