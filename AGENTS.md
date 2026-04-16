# AGENTS.md

## Build and Test

```powershell
# .NET build and test
dotnet build --configuration Release
dotnet test --configuration Release

# Dashboard build and test
cd dashboard && npm ci && npm run build && npx vitest run

# PowerShell tests (requires Pester 5+)
pwsh -Command "Import-Module Pester; $config = New-PesterConfiguration; $config.Run.Path = 'tests/PowerShell'; Invoke-Pester -Configuration $config"
```

## Project Structure

- `src/` — .NET server (API, MCP endpoint, Cosmos DB repositories)
- `dashboard/` — React/Vite SPA (MSAL auth, session/task views)
- `plugins/copilot-session-tracker/` — Installable Copilot CLI plugin (canonical source for all plugin files)
- `deploy/` — Bicep IaC and setup scripts
- `tests/` — .NET and PowerShell tests

## Plugin Sync Rule (MANDATORY)

**Any change to files under `plugins/copilot-session-tracker/` MUST be synced to the `aef123/ai-marketplace` repo.**

The plugin is distributed via the `aef123/ai-marketplace` marketplace. The canonical source is this repo (`plugins/copilot-session-tracker/`), but users install from the marketplace. After any change:

1. Clone `aef123/ai-marketplace`
2. Remove `plugins/copilot-session-tracker/` in the marketplace repo
3. Copy `plugins/copilot-session-tracker/` from this repo into it
4. Update marketplace.json if the plugin description or version changed
5. Commit and push the marketplace repo

If marketplace.json entries (in `.claude/`, `.claude-plugin/`, `.github/plugin/`) need version or description updates, update all three locations.

## Code Style

- C#: Standard .NET conventions, nullable reference types enabled
- TypeScript/React: Vitest for testing, MSAL for auth
- PowerShell: Functions use Verb-Noun naming, scripts use `$ErrorActionPreference = "Stop"`

## Boundaries

- Never hardcode personal Azure settings (tenant IDs, client IDs, subscription IDs). All values must be configurable.
- The PowerShell module reads config from `~/.copilot/copilot-tracker-config.json`. No defaults.
- Session tracking is best-effort. Never let tracker errors block user work.
