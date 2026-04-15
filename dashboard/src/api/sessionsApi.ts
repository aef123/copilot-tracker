import { apiGet } from "./apiClient";
import type { Session, PagedResult } from "./types";

export interface ListSessionsParams {
  machineId?: string;
  status?: string;
  since?: string;
  continuationToken?: string;
  pageSize?: number;
}

export async function listSessions(params: ListSessionsParams = {}): Promise<PagedResult<Session>> {
  const searchParams = new URLSearchParams();
  if (params.machineId) searchParams.set("machineId", params.machineId);
  if (params.status) searchParams.set("status", params.status);
  if (params.since) searchParams.set("since", params.since);
  if (params.continuationToken) searchParams.set("continuationToken", params.continuationToken);
  if (params.pageSize) searchParams.set("pageSize", params.pageSize.toString());

  const query = searchParams.toString();
  return apiGet<PagedResult<Session>>(`/api/sessions${query ? `?${query}` : ""}`);
}

export async function getSession(machineId: string, id: string): Promise<Session> {
  return apiGet<Session>(`/api/sessions/${encodeURIComponent(machineId)}/${encodeURIComponent(id)}`);
}
