import { useEffect, useState } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { getSession, listTasks } from "../api";
import type { Session, TrackerTask } from "../api";

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status}`}>{status}</span>;
}

function formatDate(iso?: string) {
  return iso ? new Date(iso).toLocaleString() : "-";
}

export function SessionDetail() {
  const { machineId, id } = useParams<{ machineId: string; id: string }>();
  const navigate = useNavigate();
  const [session, setSession] = useState<Session | null>(null);
  const [tasks, setTasks] = useState<TrackerTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!machineId || !id) return;

    async function load() {
      try {
        const [sessionData, tasksData] = await Promise.all([
          getSession(machineId!, id!),
          listTasks({ queueName: undefined }),
        ]);
        setSession(sessionData);
        // Filter tasks for this session client-side
        setTasks(tasksData.items.filter((t) => t.sessionId === id));
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load session");
      } finally {
        setLoading(false);
      }
    }

    load();
  }, [machineId, id]);

  if (loading) return <div className="loading">Loading session...</div>;
  if (error) return <div className="error-message">{error}</div>;
  if (!session) return <div className="empty-state">Session not found.</div>;

  const fields = [
    { label: "Session ID", value: session.id },
    { label: "Machine ID", value: session.machineId },
    { label: "Status", value: session.status, badge: true },
    { label: "Repository", value: session.repository || "-" },
    { label: "Branch", value: session.branch || "-" },
    { label: "Created", value: formatDate(session.createdAt) },
    { label: "Updated", value: formatDate(session.updatedAt) },
    { label: "Last Heartbeat", value: formatDate(session.lastHeartbeat) },
    { label: "Completed", value: formatDate(session.completedAt) },
    { label: "User ID", value: session.userId },
    { label: "Created By", value: session.createdBy },
  ];

  return (
    <div>
      <Link to="/sessions" className="back-link">
        &larr; Back to Sessions
      </Link>

      <div className="detail-card">
        <h2>Session Details</h2>
        {session.summary && <p>{session.summary}</p>}
        <div className="detail-grid">
          {fields.map((f) => (
            <div key={f.label} className="detail-field">
              <div className="label">{f.label}</div>
              <div className="value">
                {f.badge ? <StatusBadge status={String(f.value)} /> : f.value}
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="detail-card">
        <h2>Tasks ({tasks.length})</h2>
        {tasks.length === 0 ? (
          <div className="empty-state">No tasks for this session.</div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Title</th>
                <th>Source</th>
                <th>Created</th>
                <th>Result / Error</th>
              </tr>
            </thead>
            <tbody>
              {tasks.map((t) => (
                <tr
                  key={`${t.queueName}-${t.id}`}
                  onClick={() =>
                    navigate(`/tasks/${encodeURIComponent(t.queueName)}/${encodeURIComponent(t.id)}`)
                  }
                >
                  <td>
                    <StatusBadge status={t.status} />
                  </td>
                  <td>{t.title}</td>
                  <td>{t.source}</td>
                  <td>{formatDate(t.createdAt)}</td>
                  <td>{t.result || t.errorMessage || "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
