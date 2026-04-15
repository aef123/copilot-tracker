# 06 - Dashboard Architecture

## Overview

The dashboard is a React single-page application built with Vite and TypeScript. It's the primary UI for viewing sessions, tasks, and logs tracked by the Copilot Session Tracker.

In production, the dashboard is served as static files out of the ASP.NET Core server's `wwwroot/` directory. There's no separate hosting infrastructure. The .NET server handles both the API and the SPA, with fallback routing so client-side routes work on refresh.

The old dashboard talked directly to the Cosmos DB REST API using manually pasted tokens. That's gone. The new dashboard talks to the `/api/*` REST endpoints with proper MSAL.js authentication. No more copying tokens from the Azure portal.

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | React + TypeScript |
| Build Tooling | Vite |
| Authentication | MSAL.js (`@azure/msal-browser`) |
| Unit Testing | Vitest + React Testing Library |
| E2E Testing | Playwright |
| API Mocking | msw (Mock Service Worker) |

Vite was chosen over Create React App for faster builds and better dev server performance. TypeScript is non-negotiable for a project that shares type contracts with a .NET backend.

## Authentication (MSAL.js)

The dashboard uses the same Entra ID app registration as the API: **Copilot Tracker** (`4c8148f5-c913-40c5-863f-1c019821eac4`). One registration, two consumers. The SPA redirect URIs are configured for both `localhost:5173` (local dev) and the production URL.

### Token Acquisition

The dashboard acquires tokens using the scope:

```
api://4c8148f5-c913-40c5-863f-1c019821eac4/CopilotTracker.ReadWrite
```

Token flow works like this:

1. On app load, MSAL attempts silent token acquisition using cached credentials.
2. If silent acquisition succeeds, the user is logged in with no visible prompt.
3. If silent acquisition fails (first visit, expired session, etc.), MSAL triggers a popup login.
4. Once authenticated, the token is cached in browser storage and renewed silently on subsequent requests.

This replaces the old workflow where you'd grab a token from the Azure portal and paste it into the dashboard config. That was fine for a single developer but didn't scale and was a security headache.

### MSAL Configuration

The MSAL instance is initialized once at app startup with the app's client ID and authority. The `loginPopup` method handles interactive login. All API calls use `acquireTokenSilent` first, falling back to `acquireTokenPopup` if the silent call fails.

There's no client secret involved. This is a public client (SPA), so MSAL uses the authorization code flow with PKCE.

## API Client

The API client lives in `dashboard/src/api/` and provides typed methods for every server endpoint.

### Key Design Decisions

- **Relative URLs only.** All calls go to `/api/*` paths. In production, the SPA and API share the same origin, so no CORS configuration is needed. In local dev, Vite's proxy handles forwarding.
- **Automatic bearer tokens.** Every request acquires a token from MSAL and attaches it as a `Bearer` header. Callers don't think about auth.
- **Pagination with continuation tokens.** List endpoints return continuation tokens for paging through large result sets. The client handles these transparently, exposing a simple `loadMore()` pattern to components.
- **Type-safe contracts.** Request and response types in the client match the .NET model classes. If the server shape changes and the dashboard types don't get updated, TypeScript catches it at build time.

### Example Flow

```
Component calls api.getSessions({ status: "active" })
  -> API client acquires token via MSAL
  -> Sends GET /api/sessions?status=active with Bearer header
  -> Parses response into typed SessionListResponse
  -> Returns typed data to component
```

## Components

These components carry over from the existing dashboard, updated to use the new API client and MSAL auth.

### Session List

The main landing page. Displays all tracked sessions with filtering by:

- Status (active, completed, failed)
- Machine name
- Date range

Supports pagination through the continuation token pattern described above.

### Session Detail

Shows a single session's metadata (start time, machine, repository, branch) and its list of tasks. Each task shows title, status, and duration.

### Task Detail

Drills into a specific task within a session. Shows the task's log entries (progress updates, errors, output) in chronological order.

### Health/Overview Dashboard

An aggregate view with counts of active sessions, recent task completions, failure rates, and other summary metrics. Polls `/api/health` on an interval to keep data fresh. This isn't WebSocket-based real-time. It's simple polling, which is good enough for a monitoring dashboard that doesn't need sub-second updates.

### Real-Time Refresh

Components that display live data use interval-based polling. The health dashboard is the primary example, but the session list also supports a refresh interval. The polling interval is configurable but defaults to 30 seconds.

## Build and Deployment

### Directory Structure

```
dashboard/
  src/
    api/          # Typed API client
    components/   # React components
    auth/         # MSAL configuration and helpers
    ...
  dist/           # Build output (gitignored)
  vite.config.ts
  package.json
```

### Build Process

Running `npm run build` in the `dashboard/` directory produces optimized static assets in `dashboard/dist/`.

### CD Pipeline

The deployment pipeline does the following in order:

1. Install dashboard dependencies (`npm ci`).
2. Build the dashboard (`npm run build`).
3. Copy `dashboard/dist/*` into `src/CopilotTracker.Server/wwwroot/`.
4. Run `dotnet publish` on the server project.

The result is a single deployable artifact that contains both the API and the SPA. No separate CDN or static file hosting is needed.

### SPA Fallback Routing

The ASP.NET Core server is configured with fallback routing so that requests to client-side routes (like `/sessions/abc123`) don't return 404. Instead, the server returns `index.html` and lets React Router handle the path.

## Local Development

### Dev Server

For local development, the Vite dev server runs on `localhost:5173` with a proxy configuration that forwards `/api/*` requests to `localhost:5000` (the .NET server).

```
Browser -> localhost:5173 (Vite)
  Static assets -> served directly by Vite with HMR
  /api/* requests -> proxied to localhost:5000 (ASP.NET Core)
```

This gives you hot module replacement for the React code while still hitting the real API server. You get instant feedback on UI changes without waiting for a full build.

### MSAL in Local Dev

MSAL is configured with `localhost:5173` as a valid redirect URI in the Entra app registration. Login popups redirect back to the local dev server. The token scope and authority are the same as production.

### Typical Workflow

1. Start the .NET server: `dotnet run` in `src/CopilotTracker.Server/`.
2. Start the Vite dev server: `npm run dev` in `dashboard/`.
3. Open `http://localhost:5173` in the browser.
4. MSAL handles login on first visit.
5. Edit React components and see changes instantly via HMR.

## Testing Strategy

### Unit Tests (Vitest + React Testing Library)

Component-level tests that verify rendering and behavior. These don't hit real APIs. They test that components respond correctly to props, user interactions, and state changes.

Run with `npm run test` in the `dashboard/` directory.

### API Client Tests (msw)

The API client is tested using Mock Service Worker (msw) to intercept network requests. Tests verify that:

- Requests are formatted correctly (URLs, headers, query params).
- Bearer tokens are attached.
- Responses are parsed into the correct types.
- Pagination and continuation tokens work.
- Error responses are handled gracefully.

msw runs in the test process and intercepts `fetch` calls at the network level. No real server is needed.

### E2E Tests (Playwright)

Full user flow tests that run in a real browser. These cover:

- Login flow (MSAL popup, redirect back to app).
- Browsing the session list, applying filters.
- Navigating to session detail and task detail views.
- Verifying data displayed matches what the API returns.

Playwright tests run against a local instance of the full stack (ASP.NET Core server + built dashboard).

## Related Docs

- [00 - Architecture](00-architecture.md) - Overall system architecture and component overview.
- [01 - Auth Model](01-auth-model.md) - Entra ID configuration, RBAC, and token flows.
- [03 - API Design](03-api-design.md) - REST endpoint specifications the dashboard consumes.
- [04 - Test Plan](04-test-plan.md) - Full testing strategy across all components.
