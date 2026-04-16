import { apiGet } from "./apiClient";
import type { TrackerTask, TaskLog, PagedResult } from "./types";

// Re-export new prompt API for backward compatibility
export { listPrompts, getPrompt, getPromptLogs } from "./promptsApi";
export type { ListPromptsParams } from "./promptsApi";

export interface ListTasksParams {
  queueName?: string;
  status?: string;
  continuationToken?: string;
  pageSize?: number;
}

export async function listTasks(params: ListTasksParams = {}): Promise<PagedResult<TrackerTask>> {
  const searchParams = new URLSearchParams();
  if (params.queueName) searchParams.set("queueName", params.queueName);
  if (params.status) searchParams.set("status", params.status);
  if (params.continuationToken) searchParams.set("continuationToken", params.continuationToken);
  if (params.pageSize) searchParams.set("pageSize", params.pageSize.toString());

  const query = searchParams.toString();
  return apiGet<PagedResult<TrackerTask>>(`/api/tasks${query ? `?${query}` : ""}`);
}

export async function getTask(queueName: string, id: string): Promise<TrackerTask> {
  return apiGet<TrackerTask>(`/api/tasks/${encodeURIComponent(queueName)}/${encodeURIComponent(id)}`);
}

export async function getTaskLogs(
  queueName: string,
  taskId: string,
  params: { continuationToken?: string; pageSize?: number } = {}
): Promise<PagedResult<TaskLog>> {
  const searchParams = new URLSearchParams();
  if (params.continuationToken) searchParams.set("continuationToken", params.continuationToken);
  if (params.pageSize) searchParams.set("pageSize", params.pageSize.toString());

  const query = searchParams.toString();
  return apiGet<PagedResult<TaskLog>>(
    `/api/tasks/${encodeURIComponent(queueName)}/${encodeURIComponent(taskId)}/logs${query ? `?${query}` : ""}`
  );
}
