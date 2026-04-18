import { useEffect, useState } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { getSession, listPrompts } from "../api";
import type { Session, Prompt } from "../api";
import { getDisplayStatus, getTitleColorClass } from "../utils/sessionStatus";

const PROMPT_PREVIEW_LENGTH = 80;

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

function ToolBadge({ tool }: { tool?: string }) {
  const name = tool || "copilot";
  return <span className={`tool-badge ${name}`}>{name}</span>;
}

function formatDate(iso?: string) {
  return iso ? new Date(iso).toLocaleString() : "-";
}

export function SessionDetail() {
  const { machineId, id } = useParams<{ machineId: string; id: string }>();
  const navigate = useNavigate();
  const [session, setSession] = useState<Session | null>(null);
  const [prompts, setPrompts] = useState<Prompt[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!machineId || !id) return;

    async function load() {
      try {
        const [sessionData, promptsData] = await Promise.all([
          getSession(machineId!, id!),
          listPrompts({ sessionId: id }),
        ]);
        setSession(sessionData);
        setPrompts(promptsData.items);
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

  const latestPrompt = prompts.length > 0
    ? prompts.reduce((latest, p) => Number(p.hookTimestamp ?? 0) > Number(latest.hookTimestamp ?? 0) ? p : latest)
    : null;
  const hasActivePrompt = latestPrompt?.status === "started";
  const enrichedSession = { ...session, hasActivePrompt };
  const displayStatus = getDisplayStatus(enrichedSession);
  const titleColorClass = getTitleColorClass(enrichedSession);

  const fields: { label: string; value: string; badge?: boolean; toolBadge?: boolean; className?: string }[] = [
    { label: "Session ID", value: session.id },
    { label: "Title", value: session.title || "N/A", className: titleColorClass },
    { label: "Machine ID", value: session.machineId },
    { label: "Status", value: displayStatus, badge: true },
    { label: "Repository", value: session.repository || "-" },
    { label: "Branch", value: session.branch || "-" },
    { label: "Tool", value: session.tool || "copilot", toolBadge: true },
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
                {f.badge ? <StatusBadge status={String(f.value)} /> : f.toolBadge ? <ToolBadge tool={String(f.value)} /> : <span className={f.className || ""}>{f.value}</span>}
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="detail-card">
        <h2>Prompts ({prompts.length})</h2>
        {prompts.length === 0 ? (
          <div className="empty-state">No prompts for this session.</div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Title</th>
                <th>Prompt</th>
                <th>Created</th>
                <th>Result / Error</th>
              </tr>
            </thead>
            <tbody>
              {prompts.map((p) => (
                <tr
                  key={p.id}
                  onClick={() =>
                    navigate(`/prompts/${encodeURIComponent(p.sessionId)}/${encodeURIComponent(p.id)}`)
                  }
                >
                  <td>
                    <StatusBadge status={p.status} />
                  </td>
                  <td>{p.title || "-"}</td>
                  <td className="cell-prompt-preview">
                    {p.promptText
                      ? p.promptText.length > PROMPT_PREVIEW_LENGTH
                        ? p.promptText.slice(0, PROMPT_PREVIEW_LENGTH) + "..."
                        : p.promptText
                      : "-"}
                  </td>
                  <td>{formatDate(p.createdAt)}</td>
                  <td>{p.result || p.errorMessage || "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
