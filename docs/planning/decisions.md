# Architecture Decision Records

Short entries documenting significant decisions. Format: context, decision, consequences.

---

## ADR-001: Managed Identity to Cosmos DB (not OBO)

**Context:** The server needs to authenticate to Cosmos DB. Options: On-Behalf-Of (OBO) flow passing through user tokens, or managed identity where the server authenticates as itself.

**Decision:** Use a user-assigned managed identity (UAMI) on the App Service, granted Cosmos DB Built-in Data Contributor role. Validate user identity at the API/MCP layer only.

**Consequences:**
- Simpler token flow, no OBO plumbing
- Users don't need individual Cosmos DB RBAC role assignments
- Authorization logic lives in our code, not delegated to Cosmos
- Trade-off: Cosmos DB can't enforce per-user access policies (acceptable for this use case)

---

## ADR-002: User-Assigned Managed Identity (not System-Assigned)

**Context:** Managed identities come in two flavors: system-assigned (tied to the App Service lifecycle) and user-assigned (independent resource).

**Decision:** Use user-assigned managed identity (UAMI).

**Consequences:**
- Survives App Service delete/recreate
- Can be pre-provisioned in Bicep before the App Service exists
- Can be shared across resources later if needed
- Slightly more Bicep to write (create identity resource + assign to App Service)

---

## ADR-003: Single Process (MCP + API + SPA)

**Context:** The MCP server and REST API could be separate deployments or a single process.

**Decision:** Single ASP.NET Core process hosting MCP at `/mcp`, API at `/api/*`, and SPA as static fallback.

**Consequences:**
- One deployment artifact, one App Service, simpler ops
- Shared DI container, shared service layer
- Must be careful with SPA fallback routing (don't swallow `/mcp` or `/api` routes)
- If either MCP or API needs independent scaling later, would need to split

---

## ADR-004: App Service (not Container Apps)

**Context:** Hosting options: Azure App Service (traditional PaaS) vs Container Apps (container-native, scales to zero).

**Decision:** App Service. Simpler for a single .NET app, always-on avoids cold starts for MCP, background services run reliably.

**Consequences:**
- No cold-start latency for CLI interactions
- Background `IHostedService` (stale cleanup) runs continuously
- Can't scale to zero (always paying for at least B1)
- Can revisit Container Apps later if scale-to-zero becomes valuable

---

## ADR-005: Partition-Key-Aware API Routes

**Context:** Cosmos DB requires partition keys for efficient point reads. The original API design used `/api/sessions/{id}` which forces cross-partition queries.

**Decision:** Include partition key context in routes: `/api/sessions/{machineId}/{id}`, `/api/tasks/{queueName}/{id}`.

**Consequences:**
- Efficient point reads (no fan-out)
- Clients must know the partition key to fetch a specific item
- List endpoints still support cross-partition queries with filters

---

## ADR-006: Stale Cleanup as Background Service (not MCP Tool)

**Context:** Stale sessions (active but no heartbeat for >5 min) need cleanup. Could be an MCP tool, a scheduled job, or a background service.

**Decision:** `IHostedService` timer inside the host process. Runs every few minutes.

**Consequences:**
- Doesn't depend on any client calling it
- Works reliably on App Service (always-on)
- If we move to Container Apps with scale-to-zero, would need a separate scheduled job

---

## ADR-007: OIDC Federated Credentials for CI/CD (no stored secrets)

**Context:** GitHub Actions needs to deploy to Azure. Options: client secret in GitHub Secrets, or OIDC federated credentials.

**Decision:** OIDC federated credentials (workload identity federation). No secrets stored in GitHub.

**Consequences:**
- Zero secret rotation burden
- One-time setup: create service principal, configure federated credential trust
- Three GitHub Actions variables (non-secret identifiers): AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID
- Deployment config (resource group, region, SKU) lives in `deploy/main.bicepparam` in the repo

---

## ADR-008: Rebuild in Separate Repo

**Context:** The original tracker lives in `ai-marketplace/plugins/copilot-session-tracker`. The new architecture is a significant rebuild.

**Decision:** Build the new version in `github.com/aef123/copilot-tracker`. Leave the ai-marketplace version as-is.

**Consequences:**
- Clean start, no legacy baggage
- The new repo contains everything: server, dashboard, skills, deploy, docs
- The ai-marketplace plugin continues to work independently (direct Cosmos REST)
- Skills in the new repo will be MCP-based replacements
