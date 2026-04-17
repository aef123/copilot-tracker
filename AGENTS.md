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

- `src/` — .NET server (REST API, Cosmos DB repositories)
- `dashboard/` — React/Vite SPA (MSAL auth, session/task views)
- `plugins/copilot-session-tracker/` — Installable Copilot CLI plugin (canonical source for all plugin files)
- `deploy/` — Bicep IaC and setup scripts
- `tests/` — .NET and PowerShell tests

## Plugin Sync Rule (MANDATORY)

**Any change to files under `plugins/copilot-session-tracker/` MUST be synced to the `aef123/ai-marketplace` repo.**

The plugin is distributed via the `aef123/ai-marketplace` marketplace. The canonical source is this repo (`plugins/copilot-session-tracker/`), but users install from the marketplace. After any change:

### Step 1: Build and package deployment artifacts

Before syncing to the marketplace, build the server and copy artifacts into the plugin tree:

1. Build the .NET server for deployment:
   ```powershell
   dotnet publish src/CopilotTracker.Server -c Release -o publish/
   ```

2. Copy published binaries into the plugin's deploy skill:
   ```powershell
   Remove-Item -Recurse -Force plugins/copilot-session-tracker/skills/deploy/binaries/ -ErrorAction SilentlyContinue
   New-Item -ItemType Directory -Path plugins/copilot-session-tracker/skills/deploy/binaries/ -Force
   Copy-Item -Recurse publish/* plugins/copilot-session-tracker/skills/deploy/binaries/
   ```

3. Copy Bicep infrastructure templates into the plugin's deploy skill:
   ```powershell
   Remove-Item -Recurse -Force plugins/copilot-session-tracker/skills/deploy/infra/ -ErrorAction SilentlyContinue
   New-Item -ItemType Directory -Path plugins/copilot-session-tracker/skills/deploy/infra/ -Force
   Copy-Item deploy/main.bicep plugins/copilot-session-tracker/skills/deploy/infra/
   ```

4. Copy the same binaries to the update-server skill:
   ```powershell
   Remove-Item -Recurse -Force plugins/copilot-session-tracker/skills/update-server/binaries/ -ErrorAction SilentlyContinue
   New-Item -ItemType Directory -Path plugins/copilot-session-tracker/skills/update-server/binaries/ -Force
   Copy-Item -Recurse publish/* plugins/copilot-session-tracker/skills/update-server/binaries/
   ```

### Step 2: Sync to marketplace

5. Clone `aef123/ai-marketplace`
6. Remove `plugins/copilot-session-tracker/` in the marketplace repo
7. Copy `plugins/copilot-session-tracker/` from this repo into it
8. Update marketplace.json if the plugin description or version changed
9. Commit and push the marketplace repo

If marketplace.json entries (in `.claude/`, `.claude-plugin/`, `.github/plugin/`) need version or description updates, update all three locations.

> **Note:** The `binaries/` and `infra/` directories under the skill folders are NOT checked into this repo. They are only generated during the marketplace sync process. The `.gitignore` excludes them.

## Code Style

- C#: Standard .NET conventions, nullable reference types enabled
- TypeScript/React: Vitest for testing, MSAL for auth
- PowerShell: Functions use Verb-Noun naming, scripts use `$ErrorActionPreference = "Stop"`

## Boundaries

- Never hardcode personal Azure settings (tenant IDs, client IDs, subscription IDs). All values must be configurable.
- The PowerShell module reads config from `~/.copilot/copilot-tracker-config.json`. No defaults.
- Session tracking is best-effort. Never let tracker errors block user work.
