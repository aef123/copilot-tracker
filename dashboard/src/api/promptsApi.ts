import { apiGet } from "./apiClient";
import type { Prompt, PromptLog, PagedResult } from "./types";

export interface ListPromptsParams {
  sessionId?: string;
  status?: string;
  continuationToken?: string;
  pageSize?: number;
}

export async function listPrompts(params: ListPromptsParams = {}): Promise<PagedResult<Prompt>> {
  const searchParams = new URLSearchParams();
  if (params.sessionId) searchParams.set("sessionId", params.sessionId);
  if (params.status) searchParams.set("status", params.status);
  if (params.continuationToken) searchParams.set("continuationToken", params.continuationToken);
  if (params.pageSize) searchParams.set("pageSize", params.pageSize.toString());

  const query = searchParams.toString();
  return apiGet<PagedResult<Prompt>>(`/api/prompts${query ? `?${query}` : ""}`);
}

export async function getPrompt(sessionId: string, id: string): Promise<Prompt> {
  return apiGet<Prompt>(`/api/prompts/${encodeURIComponent(sessionId)}/${encodeURIComponent(id)}`);
}

export async function getPromptLogs(
  sessionId: string,
  promptId: string,
  params: { continuationToken?: string; pageSize?: number } = {}
): Promise<PagedResult<PromptLog>> {
  const searchParams = new URLSearchParams();
  if (params.continuationToken) searchParams.set("continuationToken", params.continuationToken);
  if (params.pageSize) searchParams.set("pageSize", params.pageSize.toString());

  const query = searchParams.toString();
  return apiGet<PagedResult<PromptLog>>(
    `/api/prompts/${encodeURIComponent(sessionId)}/${encodeURIComponent(promptId)}/logs${query ? `?${query}` : ""}`
  );
}
