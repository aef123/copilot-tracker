import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { listSessions } from "../api";
import type { Session } from "../api";
import { getDisplayStatus, getTitleColorClass } from "../utils/sessionStatus";

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

function ToolBadge({ tool }: { tool?: string }) {
  const name = tool || "copilot";
  return <span className={`tool-badge ${name}`}>{name}</span>;
}

function formatRepository(repo: string | undefined): string {
  if (!repo) return "-";
  let name = repo;
  if (name.endsWith(".git")) {
    name = name.slice(0, -4);
  }
  const lastSlash = name.lastIndexOf("/");
  if (lastSlash >= 0) {
    name = name.substring(lastSlash + 1);
  }
  return name || "-";
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("active");

  const fetchData = useCallback(async () => {
    try {
      const params: Record<string, string | number | undefined> = { pageSize: 100 };
      if (statusFilter === "active") {
        params.statusGroup = "live";
      } else if (statusFilter === "stale") {
        params.statusGroup = "stale";
      }
      const result = await listSessions(params);
      const sorted = result.items.sort(
        (a, b) => new Date(b.lastHeartbeat).getTime() - new Date(a.lastHeartbeat).getTime()
      );
      setSessions(sorted);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load sessions");
    } finally {
      setLoading(false);
    }
  }, [statusFilter]);

  useEffect(() => {
    let timeoutId: ReturnType<typeof setTimeout>;
    let cancelled = false;

    const refresh = async () => {
      await fetchData();
      if (!cancelled) {
        timeoutId = setTimeout(refresh, 60_000);
      }
    };

    refresh();

    return () => {
      cancelled = true;
      clearTimeout(timeoutId);
    };
  }, [fetchData]);

  if (loading) return <div className="loading">Loading sessions...</div>;
  if (error) return <div className="error-message">{error}</div>;

  return (
    <div>
      <h2>Sessions Overview</h2>
      <div className="filters">
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} aria-label="Filter by status">
          <option value="active">Active</option>
          <option value="stale">Stale</option>
          <option value="all">All</option>
        </select>
      </div>
      {sessions.length === 0 ? (
        <div className="empty-state">No sessions found.</div>
      ) : (
        <div className="session-grid">
          {sessions.map((s) => (
              <div
                key={`${s.machineId}-${s.id}`}
                className={`session-card ${s.hasActivePrompt ? "session-card-active" : ""}`}
                onClick={() =>
                  navigate(
                    `/sessions/${encodeURIComponent(s.machineId)}/${encodeURIComponent(s.id)}`
                  )
                }
              >
                <div className="session-card-header">
                  <div style={{ display: "flex", gap: "6px", alignItems: "center" }}>
                    <StatusBadge status={getDisplayStatus(s)} />
                    <ToolBadge tool={s.tool} />
                  </div>
                  <span className="session-card-heartbeat">{timeAgo(s.lastHeartbeat)}</span>
                </div>

                <div className={`session-card-title ${getTitleColorClass(s)}`}>
                  {s.title || "N/A"}
                </div>

                <div className="session-card-machine">{s.machineId}</div>

                <div className="session-card-meta">
                  {s.repository && (
                    <span className="session-card-repo">{formatRepository(s.repository)}</span>
                  )}
                  {s.branch && (
                    <span className="session-card-branch">{s.branch}</span>
                  )}
                </div>

                <div className="session-card-times">
                  <span>Created {timeAgo(s.createdAt)}</span>
                </div>
              </div>
            ))}
        </div>
      )}
    </div>
  );
}
