import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { getTask, getTaskLogs } from "../api";
import type { TrackerTask, TaskLog } from "../api";

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status}`}>{status}</span>;
}

function formatDate(iso?: string) {
  return iso ? new Date(iso).toLocaleString() : "-";
}

export function TaskDetail() {
  const { queueName, id } = useParams<{ queueName: string; id: string }>();
  const [task, setTask] = useState<TrackerTask | null>(null);
  const [logs, setLogs] = useState<TaskLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!queueName || !id) return;

    async function load() {
      try {
        const [taskData, logsData] = await Promise.all([
          getTask(queueName!, id!),
          getTaskLogs(queueName!, id!),
        ]);
        setTask(taskData);
        setLogs(logsData.items.sort((a, b) => a.timestamp.localeCompare(b.timestamp)));
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load task");
      } finally {
        setLoading(false);
      }
    }

    load();
  }, [queueName, id]);

  if (loading) return <div className="loading">Loading task...</div>;
  if (error) return <div className="error-message">{error}</div>;
  if (!task) return <div className="empty-state">Task not found.</div>;

  const fields = [
    { label: "Task ID", value: task.id },
    { label: "Session ID", value: task.sessionId },
    { label: "Queue", value: task.queueName },
    { label: "Status", value: task.status, badge: true },
    { label: "Title", value: task.title },
    { label: "Source", value: task.source },
    { label: "Created", value: formatDate(task.createdAt) },
    { label: "Updated", value: formatDate(task.updatedAt) },
    { label: "User ID", value: task.userId },
    { label: "Created By", value: task.createdBy },
  ];

  return (
    <div>
      <Link to="/sessions" className="back-link">
        &larr; Back to Sessions
      </Link>

      <div className="detail-card">
        <h2>Task Details</h2>
        <div className="detail-grid">
          {fields.map((f) => (
            <div key={f.label} className="detail-field">
              <div className="label">{f.label}</div>
              <div className="value">
                {f.badge ? <StatusBadge status={String(f.value)} /> : f.value}
              </div>
            </div>
          ))}
          {task.result && (
            <div className="detail-field">
              <div className="label">Result</div>
              <div className="value">{task.result}</div>
            </div>
          )}
          {task.errorMessage && (
            <div className="detail-field">
              <div className="label">Error</div>
              <div className="value" style={{ color: "#dc2626" }}>
                {task.errorMessage}
              </div>
            </div>
          )}
        </div>
      </div>

      <div className="detail-card">
        <h2>Logs ({logs.length})</h2>
        {logs.length === 0 ? (
          <div className="empty-state">No log entries.</div>
        ) : (
          logs.map((log) => (
            <div key={log.id} className="log-entry">
              <span className="log-time">{formatDate(log.timestamp)}</span>
              <StatusBadge status={log.logType} />
              <span className="log-message">{log.message}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
