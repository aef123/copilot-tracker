# Auth Model

## Overview

The Copilot Tracker uses a two-tier auth model. The edge layer (CLI and Dashboard) authenticates users with Entra ID delegated tokens. The server layer talks to Cosmos DB using a user-assigned managed identity. These two tiers are deliberately separate. Users prove who they are at the edge, and the server handles data access with its own identity. Authorization logic lives in our code, not in Cosmos DB RBAC assignments per user.

A single Entra app registration, "Copilot Tracker," handles both the CLI and Dashboard auth flows. The sign-in audience is `AzureADMyOrg` (single tenant), scoped to tenant `5df6d88f-0d78-491b-9617-8b43a209ba73`.

## Edge Auth

Every request to the MCP server or REST API must carry a valid bearer token issued by Entra ID. The server validates the token, extracts the caller's identity (object ID, display name, email), and uses that identity for authorization decisions and audit trails.

The Entra app registration exposes a single API scope:

```
api://4c8148f5-c913-40c5-863f-1c019821eac4/CopilotTracker.ReadWrite
```

### CLI

The CLI acquires tokens using the Azure CLI's built-in token acquisition:

```bash
az account get-access-token --resource api://4c8148f5-c913-40c5-863f-1c019821eac4
```

This piggybacks on the user's existing `az login` session. No separate login flow, no client secrets, no device code prompts. If you're logged into the Azure CLI, you can talk to the tracker.

### Dashboard (SPA)

The Dashboard is a single-page application that uses MSAL.js to acquire tokens interactively. This replaces the old system where users had to manually paste tokens, which was a terrible experience.

MSAL.js handles the full OAuth 2.0 authorization code flow with PKCE. The user signs in through the Entra login page, MSAL gets the tokens, and the Dashboard attaches them to API requests automatically.

**Registered redirect URIs:**

| Environment | URI |
|---|---|
| Local dev | `http://localhost:5173` |
| Local dev callback | `http://localhost:5173/auth/callback` |
| Production | `https://copilot-tracker.azurewebsites.net` |
| Production callback | `https://copilot-tracker.azurewebsites.net/auth/callback` |

## Server to Cosmos Auth

The server authenticates to Cosmos DB using a user-assigned managed identity (UAMI). No connection strings. No keys. No secrets of any kind.

The UAMI is:

- Created via Bicep as part of the infrastructure deployment
- Assigned to the App Service
- Granted the **Cosmos DB Built-in Data Contributor** role on the Cosmos DB account

In code, the server uses `DefaultAzureCredential` with `ManagedIdentityClientId` explicitly set to the UAMI's client ID. This tells the SDK which identity to use when multiple are available.

```csharp
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = "<UAMI client ID>"
});
```

### Why UAMI Over System-Assigned

We chose a user-assigned managed identity over a system-assigned one for three reasons:

1. **Survives delete/recreate.** If the App Service gets deleted and recreated (common during infra changes), a system-assigned identity dies with it. Every RBAC role assignment has to be redone. A UAMI is an independent resource that persists.
2. **Can be pre-provisioned.** The UAMI and its Cosmos DB role assignment can be set up in Bicep before the App Service even exists. No chicken-and-egg deployment ordering issues.
3. **Can be shared.** If we add more compute resources later (Functions, a second App Service, etc.), the same UAMI works across all of them without duplicating role assignments.

See [ADR-003](decisions.md) for the full decision record.

## Why Not OBO

On-Behalf-Of (OBO) flow would let the server exchange the user's token for a Cosmos DB-scoped token and access Cosmos as the user. We considered it and rejected it. Here's why.

**OBO adds complexity for no real benefit.** Every request would need a token exchange. That's an extra round trip to Entra ID, plus error handling for token exchange failures, consent issues, and conditional access policies. It's a lot of plumbing.

**OBO requires per-user Cosmos DB RBAC assignments.** Every user who touches the system would need an individual Cosmos DB role assignment. That's an operational burden that scales with headcount and doesn't buy us anything, because we're enforcing authorization in our own code anyway.

**Managed identity is simpler and just as secure.** It's zero-secret (no keys to rotate or leak), auto-rotated by Azure, and works identically regardless of which user initiated the request. The server is the only principal that talks to Cosmos DB, and it uses a single, well-scoped identity to do it.

**Authorization still happens.** The fact that we don't delegate authorization to Cosmos doesn't mean it's absent. The API and MCP layers enforce who can read and write what, based on the caller's identity extracted from their Entra token. Cosmos just stores data. Our code decides who gets access.

See [ADR-004](decisions.md) for the full decision record.

## User Traceability

Even though the server uses its own identity to write to Cosmos, we don't lose track of who did what. The caller's Entra object ID and display name are extracted from the validated bearer token and stamped into audit fields on every document write:

- `createdBy` / `updatedBy`: Display name of the caller
- `userId`: Entra object ID of the caller

This gives us a complete audit trail without needing per-user Cosmos DB identities.

## Token Validation

Token validation in ASP.NET Core uses `Microsoft.Identity.Web`. The configuration is straightforward:

- **ValidateIssuer:** On. Tokens must come from our tenant.
- **ValidateAudience:** On. Tokens must be scoped to our app registration.
- **ValidateLifetime:** On. Expired tokens are rejected.

Claims are extracted via middleware and made available to controllers and MCP tools through the standard ASP.NET Core `HttpContext.User` principal. No custom token parsing, no manual JWT decoding.

## Local Development

For local development, the same `DefaultAzureCredential` chain handles auth to Cosmos DB. Since there's no managed identity on a dev machine, it falls back to the `az login` identity. As long as the developer's Entra account has the Cosmos DB Built-in Data Contributor role on the dev/shared Cosmos DB account, everything works the same way.

The CLI token acquisition (`az account get-access-token`) also uses the local `az login` session, so the developer experience is consistent across both tiers.

For the Dashboard, MSAL.js redirects to `http://localhost:5173`, which is already registered as a redirect URI.

## Related Docs

- [Architecture Overview](00-architecture.md)
- [Architecture Decision Records](decisions.md) (ADR-003: UAMI over system-assigned, ADR-004: Managed identity to Cosmos)
