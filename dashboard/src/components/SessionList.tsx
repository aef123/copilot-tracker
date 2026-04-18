import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { listSessions, listPrompts } from "../api";
import type { Session } from "../api";
import { getDisplayStatus, getTitleColorClass } from "../utils/sessionStatus";

const PROMPT_PREVIEW_LENGTH = 80;

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{status}</span>;
}

function ToolBadge({ tool }: { tool?: string }) {
  const name = tool || "copilot";
  return <span className={`tool-badge ${name}`}>{name}</span>;
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString();
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

export function SessionList() {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("active");
  const [toolFilter, setToolFilter] = useState("");
  const [machineFilter, setMachineFilter] = useState("");
  const [continuationToken, setContinuationToken] = useState<string | undefined>();
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [latestPrompts, setLatestPrompts] = useState<Record<string, string>>({});

  const fetchSessions = useCallback(
    async (token?: string) => {
      try {
        const params: Record<string, string | undefined> = {
          tool: toolFilter || undefined,
          machineId: machineFilter || undefined,
          continuationToken: token,
        };
        if (statusFilter === "active") {
          params.statusGroup = "live";
        } else if (statusFilter === "stale") {
          params.statusGroup = "stale";
        }
        const result = await listSessions(params);

        const newSessions = token ? result.items : result.items;
        if (token) {
          setSessions((prev) => [...prev, ...newSessions]);
        } else {
          setSessions(newSessions);
        }
        setContinuationToken(result.continuationToken);
        setHasMore(result.hasMore);
        setError(null);

        // Fetch latest prompt for each new session
        const promptResults = await Promise.allSettled(
          newSessions.map((s) => listPrompts({ sessionId: s.id, pageSize: 1 }))
        );
        const promptMap: Record<string, string> = {};
        newSessions.forEach((s, i) => {
          const r = promptResults[i];
          if (r.status === "fulfilled" && r.value.items.length > 0) {
            promptMap[s.id] = r.value.items[0].promptText || r.value.items[0].title || "";
          }
        });
        setLatestPrompts((prev) => ({ ...prev, ...promptMap }));
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load sessions");
      } finally {
        setLoading(false);
        setLoadingMore(false);
      }
    },
    [statusFilter, toolFilter, machineFilter]
  );

  useEffect(() => {
    setLoading(true);
    setSessions([]);
    setContinuationToken(undefined);

    let timeoutId: ReturnType<typeof setTimeout>;
    let cancelled = false;

    const refresh = async () => {
      await fetchSessions();
      if (!cancelled) {
        timeoutId = setTimeout(refresh, 60_000);
      }
    };

    refresh();

    return () => {
      cancelled = true;
      clearTimeout(timeoutId);
    };
  }, [fetchSessions]);

  const handleLoadMore = () => {
    setLoadingMore(true);
    fetchSessions(continuationToken);
  };

  if (loading) return <div className="loading">Loading sessions...</div>;
  if (error) return <div className="error-message">{error}</div>;

  return (
    <div>
      <h2>Sessions</h2>
      <div className="filters">
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          aria-label="Filter by status"
        >
          <option value="active">Active</option>
          <option value="stale">Stale</option>
          <option value="all">All</option>
        </select>
        <select
          value={toolFilter}
          onChange={(e) => setToolFilter(e.target.value)}
          aria-label="Filter by tool"
        >
          <option value="">All tools</option>
          <option value="copilot">Copilot</option>
          <option value="claude">Claude</option>
        </select>
        <input
          type="text"
          placeholder="Filter by machine ID..."
          value={machineFilter}
          onChange={(e) => setMachineFilter(e.target.value)}
          aria-label="Filter by machine ID"
        />
      </div>

      {sessions.length === 0 ? (
        <div className="empty-state">No sessions found.</div>
      ) : (
        <>
          <table className="data-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Prompt</th>
                <th>Machine ID</th>
                <th>Tool</th>
                <th>Repository</th>
                <th>Branch</th>
                <th>Created</th>
                <th>Last Heartbeat</th>
              </tr>
            </thead>
            <tbody>
              {sessions.map((s) => (
                <tr
                  key={`${s.machineId}-${s.id}`}
                  onClick={() => navigate(`/sessions/${encodeURIComponent(s.machineId)}/${encodeURIComponent(s.id)}`)}
                >
                  <td>
                    <StatusBadge status={getDisplayStatus(s)} />
                  </td>
                  <td className={`cell-prompt-preview ${getTitleColorClass(s)}`}>
                    {latestPrompts[s.id]
                      ? latestPrompts[s.id].length > PROMPT_PREVIEW_LENGTH
                        ? latestPrompts[s.id].slice(0, PROMPT_PREVIEW_LENGTH) + "..."
                        : latestPrompts[s.id]
                      : "-"}
                  </td>
                  <td>{s.machineId}</td>
                  <td><ToolBadge tool={s.tool} /></td>
                  <td>{formatRepository(s.repository)}</td>
                  <td>{s.branch || "-"}</td>
                  <td>{formatDate(s.createdAt)}</td>
                  <td>{formatDate(s.lastHeartbeat)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {hasMore && (
            <div style={{ textAlign: "center", marginTop: "1rem" }}>
              <button className="btn-primary" onClick={handleLoadMore} disabled={loadingMore}>
                {loadingMore ? "Loading..." : "Load More"}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
