import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { listSessions } from "../api";
import type { Session } from "../api";

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status}`}>{status}</span>;
}

function ToolBadge({ tool }: { tool?: string }) {
  const name = tool || "copilot";
  return <span className={`tool-badge ${name}`}>{name}</span>;
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString();
}

export function SessionList() {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState("");
  const [toolFilter, setToolFilter] = useState("");
  const [machineFilter, setMachineFilter] = useState("");
  const [continuationToken, setContinuationToken] = useState<string | undefined>();
  const [hasMore, setHasMore] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);

  const fetchSessions = useCallback(
    async (token?: string) => {
      try {
        const result = await listSessions({
          status: statusFilter || undefined,
          tool: toolFilter || undefined,
          machineId: machineFilter || undefined,
          continuationToken: token,
        });

        if (token) {
          setSessions((prev) => [...prev, ...result.items]);
        } else {
          setSessions(result.items);
        }
        setContinuationToken(result.continuationToken);
        setHasMore(result.hasMore);
        setError(null);
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
    fetchSessions();
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
          <option value="">All statuses</option>
          <option value="active">Active</option>
          <option value="idle">Idle</option>
          <option value="closed">Closed</option>
          <option value="stale">Stale</option>
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
                <th>Title</th>
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
                    <StatusBadge status={s.status} />
                  </td>
                  <td>{s.title || "-"}</td>
                  <td>{s.machineId}</td>
                  <td><ToolBadge tool={s.tool} /></td>
                  <td>{s.repository || "-"}</td>
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
