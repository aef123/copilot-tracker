export interface Session {
  id: string;
  machineId: string;
  repository?: string;
  branch?: string;
  title?: string;
  tool?: string;
  status: "active" | "idle" | "stale" | "closed";
  createdAt: string;
  updatedAt: string;
  lastHeartbeat: string;
  completedAt?: string;
  hasActivePrompt?: boolean;
  summary?: string;
  userId: string;
  createdBy: string;
}

export interface Prompt {
  id: string;
  sessionId: string;
  queueName: string;
  title: string;
  promptText?: string;
  cwd?: string;
  status: "started" | "done" | "failed";
  result?: string;
  errorMessage?: string;
  source: "prompt" | "queue";
  createdAt: string;
  updatedAt: string;
  hookTimestamp?: string;
  userId: string;
  createdBy: string;
}

/** @deprecated Use Prompt instead */
export type TrackerTask = Prompt;

export interface PromptLog {
  id: string;
  promptId: string;
  logType: "status_change" | "progress" | "output" | "error" | "heartbeat" | "subagent_start" | "subagent_stop" | "notification" | "agent_stop";
  message: string;
  timestamp: string;
  agentName?: string;
  notificationType?: string;
  hookTimestamp?: string;
}

/** @deprecated Use PromptLog instead */
export type TaskLog = PromptLog;

export interface PagedResult<T> {
  items: T[];
  continuationToken?: string;
  hasMore: boolean;
}

export interface HealthSummary {
  activeSessions: number;
  completedSessions: number;
  staleSessions: number;
  totalTasks: number;
  activeTasks: number;
  totalPrompts?: number;
  activePrompts?: number;
  timestamp: string;
}
