import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { listSessions, listPrompts } from "../api";
import type { Session, Prompt } from "../api";

const PROMPT_PREVIEW_LENGTH = 120;

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status}`}>{status}</span>;
}

function ToolBadge({ tool }: { tool?: string }) {
  const name = tool || "copilot";
  return <span className={`tool-badge ${name}`}>{name}</span>;
}

function timeAgo(iso: string): string {
  const seconds = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
  return `${Math.floor(seconds / 86400)}d ago`;
}

export function SessionGrid() {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<Session[]>([]);
  const [promptMap, setPromptMap] = useState<Record<string, Prompt | null>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    try {
      const result = await listSessions({ pageSize: 100 });
      const sorted = result.items.sort(
        (a, b) => new Date(b.lastHeartbeat).getTime() - new Date(a.lastHeartbeat).getTime()
      );
      setSessions(sorted);

      const map: Record<string, Prompt | null> = {};
      await Promise.all(
        sorted.map(async (s) => {
          try {
            const prompts = await listPrompts({ sessionId: s.id, pageSize: 1 });
            map[s.id] = prompts.items.length > 0 ? prompts.items[0] : null;
          } catch {
            map[s.id] = null;
          }
        })
      );
      setPromptMap(map);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load sessions");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 30_000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading sessions...</div>;
  if (error) return <div className="error-message">{error}</div>;

  return (
    <div>
      <h2>Sessions Overview</h2>
      {sessions.length === 0 ? (
        <div className="empty-state">No sessions found.</div>
      ) : (
        <div className="session-grid">
          {sessions.map((s) => {
            const recentPrompt = promptMap[s.id];
            const promptPreview = recentPrompt?.promptText
              ? recentPrompt.promptText.length > PROMPT_PREVIEW_LENGTH
                ? recentPrompt.promptText.slice(0, PROMPT_PREVIEW_LENGTH) + "..."
                : recentPrompt.promptText
              : null;

            return (
              <div
                key={`${s.machineId}-${s.id}`}
                className={`session-card ${s.status === "active" ? "session-card-active" : ""}`}
                onClick={() =>
                  navigate(
                    `/sessions/${encodeURIComponent(s.machineId)}/${encodeURIComponent(s.id)}`
                  )
                }
              >
                <div className="session-card-header">
                  <div style={{ display: "flex", gap: "6px", alignItems: "center" }}>
                    <StatusBadge status={s.status} />
                    <ToolBadge tool={s.tool} />
                  </div>
                  <span className="session-card-heartbeat">{timeAgo(s.lastHeartbeat)}</span>
                </div>

                <div className="session-card-machine">{s.machineId}</div>

                <div className="session-card-meta">
                  {s.repository && (
                    <span className="session-card-repo">{s.repository}</span>
                  )}
                  {s.branch && (
                    <span className="session-card-branch">{s.branch}</span>
                  )}
                </div>

                <div className="session-card-times">
                  <span>Created {timeAgo(s.createdAt)}</span>
                  {s.completedAt && <span>Completed {timeAgo(s.completedAt)}</span>}
                </div>

                {promptPreview && (
                  <div className="session-card-prompt">
                    <div className="session-card-prompt-label">Latest prompt</div>
                    <div className="session-card-prompt-text">{promptPreview}</div>
                  </div>
                )}

                {!promptPreview && (
                  <div className="session-card-prompt">
                    <div className="session-card-prompt-text session-card-prompt-empty">
                      No prompts yet
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
