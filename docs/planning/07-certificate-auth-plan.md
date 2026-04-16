# Change 1: Certificate-Based Authentication

## Problem

The current tracker authenticates exclusively via `az account get-access-token`, which requires the user to be logged in via Azure CLI. This is:
- Slow (2-5 seconds per token acquisition)
- User-dependent (requires interactive login)
- Incompatible with headless/service scenarios
- Not suitable for hooks (which need fast, automatic auth)

## Approach

Add support for app+certificate authentication alongside the existing user-based auth. The PowerShell module acquires a token using a certificate installed in the user's cert store and an Entra App Registration's client credentials. This is configured during `initialize-machine` and stored in the tracker config.

The server already uses Microsoft.Identity.Web which validates bearer tokens. App tokens (client_credentials) are valid bearer tokens with the correct audience. The server needs minimal changes to handle the fact that app tokens don't carry a `name` claim.

## Credentials (Test)

| Field | Value |
|-------|-------|
| Certificate Subject | CN=client.copilottracker.andrewfaust.com |
| Certificate Store | CurrentUser\My |
| App ClientID | 64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e |
| App ObjectID | f6a50f9f-3bcc-4ca2-b4c8-27c547464846 |
| Entra Tenant | 5df6d88f-0d78-491b-9617-8b43a209ba73 |
| App Display Name | Copilot Tracker Client |
| API Resource | api://4c8148f5-c913-40c5-863f-1c019821eac4 |

## Changes Required

### 1. Server: UserContext Enhancement

**File:** `src/CopilotTracker.Server/Auth/UserContext.cs`

Current behavior:
- Extracts `oid` claim → userId
- Extracts `name` claim → displayName
- Falls back to "anonymous"

New behavior:
- For user tokens: same as current (oid + name)
- For app tokens: `oid` is the service principal's object ID, `name` is absent
  - Use `azp` or `appid` claim for identification
  - Use `app_displayname` claim if present, else fall back to "App: {clientId}"
- Add a `CallerType` property: `User` or `Application`

### 2. Server: Auth Configuration

**File:** `src/CopilotTracker.Server/Auth/EntraAuthExtensions.cs`

The existing Microsoft.Identity.Web configuration should already accept app tokens if:
- The token has the correct `aud` (audience) matching the API's client ID
- The issuer is the expected Entra tenant

We may need to ensure `AllowWebApiToBeAuthorizedByACL = true` in the JWT bearer options so that client_credentials tokens (which have no user and no roles/scopes by default) are not rejected.

Alternatively, add an app role to the API app registration and assign it to the client app.

**Decision:** Use `AllowWebApiToBeAuthorizedByACL = true` for simplicity. This allows tokens with the correct audience and issuer to pass validation even without specific scopes. For a personal tracker tool, this is appropriate.

### 3. PowerShell Module: Certificate Token Acquisition

**File:** `plugins/copilot-session-tracker/shared/CopilotTracker.psm1`

Add function `Get-CertificateToken`:

```
1. Load config to get authMode, clientId, certificateSubject, tenantId, resourceId
2. Check for cached token (in-memory or temp file) - if valid (>5 min until expiry), return it
3. Find certificate in Cert:\CurrentUser\My by subject
4. Create JWT client assertion:
   - Header: { alg: RS256, typ: JWT, x5t: base64url(cert.GetCertHash()) }
   - Payload: { iss: clientId, sub: clientId, aud: https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token, exp: now+10min, iat: now, nbf: now, jti: guid }
   - Sign with cert private key using RSA-SHA256
5. POST to https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
   - grant_type=client_credentials
   - client_id={clientId}
   - client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
   - client_assertion={jwt}
   - scope=api://{resourceId}/.default
6. Cache token with expiry
7. Return access_token
```

Update `Get-TrackerHeaders`:
```
if config.authMode == "certificate":
    token = Get-CertificateToken
else:
    token = az account get-access-token ...
```

### 4. PowerShell Module: Token Cache

Implement a simple file-based token cache at `~/.copilot/copilot-tracker/.token-cache.json`:
```json
{
  "accessToken": "eyJ...",
  "expiresOn": "2026-04-17T00:00:00Z"
}
```

- Check cache before acquiring new token
- Refresh if token expires in < 5 minutes
- This is critical for hooks where speed matters

### 5. Config File Update

**File:** `~/.copilot/copilot-tracker-config.json`

Current fields:
```json
{
  "serverUrl": "https://copilot-tracker.azurewebsites.net",
  "tenantId": "...",
  "resourceId": "api://..."
}
```

New fields (when authMode is "certificate"):
```json
{
  "serverUrl": "https://copilot-tracker.azurewebsites.net",
  "tenantId": "...",
  "resourceId": "api://...",
  "authMode": "certificate",
  "clientId": "64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e",
  "certificateSubject": "CN=client.copilottracker.andrewfaust.com"
}
```

When authMode is "user" (or absent for backward compatibility):
```json
{
  "serverUrl": "https://copilot-tracker.azurewebsites.net",
  "tenantId": "...",
  "resourceId": "api://...",
  "authMode": "user"
}
```

### 6. Initialize-Machine Skill Update

**File:** `plugins/copilot-session-tracker/skills/initialize-machine/SKILL.md`

After gathering the base parameters (serverUrl, tenantId, resourceId), add:

1. Ask: "Do you want to authenticate using an app and certificate? (true/false)"
2. If true:
   - Ask for certificate subject name (e.g., CN=client.copilottracker.andrewfaust.com)
   - Ask for App ClientID (e.g., 64aa6bbc-0f8a-47e4-bc02-fd7ed6659b3e)
   - Verify: Certificate exists in Cert:\CurrentUser\My with matching subject
   - Verify: Can acquire token using the certificate
   - Write config with authMode="certificate"
3. If false:
   - Verify: `az account get-access-token` works
   - Write config with authMode="user"

## Test Plan

### Unit Tests

1. **UserContext with app token** - Token has `oid` and `azp` but no `name` → CallerType=Application, displayName="App: {clientId}"
2. **UserContext with user token** - Token has `oid` and `name` → CallerType=User, displayName from name claim
3. **UserContext with missing claims** - Fallback to "anonymous"

### PowerShell Tests

1. **Get-CertificateToken with valid cert** - Mock cert store, mock HTTP call, verify JWT structure
2. **Get-CertificateToken with missing cert** - Should throw clear error
3. **Token cache hit** - Cached token not expired → return cached, no HTTP call
4. **Token cache miss** - Cached token expired → acquire new token, update cache
5. **Get-TrackerHeaders with certificate mode** - Uses Get-CertificateToken
6. **Get-TrackerHeaders with user mode** - Uses az account get-access-token

### Integration Tests

1. **App token accepted by server** - POST with client_credentials token → 200
2. **User token accepted by server** - POST with user token → 200
3. **Invalid token rejected** - POST with bad token → 401

## Files Changed

| File | Change |
|------|--------|
| `src/CopilotTracker.Server/Auth/UserContext.cs` | Handle app tokens, add CallerType |
| `src/CopilotTracker.Server/Auth/EntraAuthExtensions.cs` | AllowWebApiToBeAuthorizedByACL |
| `plugins/.../shared/CopilotTracker.psm1` | Add Get-CertificateToken, update Get-TrackerHeaders |
| `plugins/.../skills/initialize-machine/SKILL.md` | Add cert auth setup flow |
| `tests/.../Auth/UserContextTests.cs` | New test cases for app tokens |
| `tests/PowerShell/CopilotTracker.Tests.ps1` | New tests for cert auth |

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Certificate not in store at runtime | Clear error message with instructions |
| Token acquisition latency in hooks | File-based token cache, refresh proactively |
| App token rejected by server | App role auth + verify in integration test |
| Breaking existing user auth | authMode defaults to "user" if absent in config |

---

## Review Feedback Incorporated

### RF-1: Replace AllowWebApiToBeAuthorizedByACL with App Role Auth

Instead of opening the API to any valid app token, we will:
1. Define an app role (e.g., `TrackerClient.ReadWrite`) on the API app registration
2. Assign this role to the client app's service principal
3. Server validates the `roles` claim contains the required role for app tokens
4. User tokens continue to work via the existing `CopilotTracker.ReadWrite` scope

This is handled in `EntraAuthExtensions.cs` with a policy that accepts either:
- A user token with the `CopilotTracker.ReadWrite` scope, OR
- An app token with the `TrackerClient.ReadWrite` role

### RF-2: Fix JWT Client Assertion

- Scope must be `{resourceId}/.default` where resourceId is already `api://...` - avoid double prefix
- Use RS256 + x5t (SHA-1 base64url) which is widely supported by Entra
- Store **thumbprint** in config alongside subject for unambiguous cert lookup
- Enforce: exactly one matching cert, valid dates, has private key

### RF-3: App Token Detection in UserContext

Detect app-only tokens by checking:
- Absence of `scp` (scope) claim AND absence of `http://schemas.microsoft.com/identity/claims/scope` claim
- If no scope claim → app token, use `azp`/`appid` for identity
- If scope claim present → user token, use `oid` + `name`
- Preserve fail-closed behavior: authenticated requests with unusable claims should fail, not fall back to anonymous

### RF-4: Token Cache Keying

Cache file includes metadata for validation:
```json
{
  "accessToken": "eyJ...",
  "expiresOn": "2026-04-17T00:00:00Z",
  "tenantId": "...",
  "clientId": "...",
  "resourceId": "...",
  "authMode": "certificate"
}
```
On cache read, validate all metadata fields match current config. Use atomic replace (write to temp file, then rename) to handle concurrent hooks.

### RF-5: Heartbeat Uses Same Token Path

The heartbeat job (in CopilotTracker.psm1 or hook-based) must call the shared `Get-TrackerToken` function rather than directly calling `az account get-access-token`.
