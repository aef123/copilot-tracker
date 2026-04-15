# Code Conventions

Project-specific patterns and rules. Updated as conventions are established.

## General

- **.NET 10** for all backend code
- **TypeScript** for dashboard (React + Vite)
- **PowerShell** for CLI skills and deployment scripts

## Backend (.NET)

- Repository interfaces in `CopilotTracker.Core/Interfaces/` -- never leak Cosmos SDK types (no `FeedIterator`, no `PartitionKey` in signatures)
- Services in `CopilotTracker.Core/Services/` -- all business logic lives here, not in controllers or MCP tools
- Controllers and MCP tools are thin -- validate input, call a service, return result
- Use `DefaultAzureCredential` with explicit `ManagedIdentityClientId` for Cosmos DB auth
- Continuation tokens for pagination, not page numbers
- Caller identity (Entra object ID, display name) stamped into audit fields on every write

## Dashboard

- Vitest for unit tests, Playwright for E2E
- msw (Mock Service Worker) for mocking API calls in tests
- MSAL.js for authentication

## Testing

- xUnit + Moq for backend unit tests
- WebApplicationFactory for API integration tests
- Cosmos DB Emulator (Docker) for repo integration tests
- Test naming: `MethodName_Scenario_ExpectedBehavior`

## Documentation

- `docs/planning/` -- human-readable planning docs (architecture, design, progress)
- `context/` -- machine-readable context for AI session continuity
- Each planning doc is self-contained (can be read independently)
- `phase-status.md` updated after each work item completes
- `CONTEXT.md` updated after each phase or significant milestone
