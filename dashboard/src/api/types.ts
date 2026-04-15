export interface Session {
  id: string;
  machineId: string;
  repository?: string;
  branch?: string;
  status: "active" | "completed" | "stale";
  createdAt: string;
  updatedAt: string;
  lastHeartbeat: string;
  completedAt?: string;
  summary?: string;
  userId: string;
  createdBy: string;
}

export interface TrackerTask {
  id: string;
  sessionId: string;
  queueName: string;
  title: string;
  status: "started" | "done" | "failed";
  result?: string;
  errorMessage?: string;
  source: "prompt" | "queue";
  createdAt: string;
  updatedAt: string;
  userId: string;
  createdBy: string;
}

export interface TaskLog {
  id: string;
  taskId: string;
  logType: "status_change" | "progress" | "output" | "error" | "heartbeat";
  message: string;
  timestamp: string;
}

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
  timestamp: string;
}
