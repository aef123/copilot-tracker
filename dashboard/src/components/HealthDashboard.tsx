import { useEffect, useState, useCallback } from "react";
import { getHealth } from "../api";
import type { HealthSummary } from "../api";

export function HealthDashboard() {
  const [health, setHealth] = useState<HealthSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchHealth = useCallback(async () => {
    try {
      const data = await getHealth();
      setHealth(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load health data");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchHealth();
    const interval = setInterval(fetchHealth, 30_000);
    return () => clearInterval(interval);
  }, [fetchHealth]);

  if (loading) return <div className="loading">Loading dashboard...</div>;
  if (error) return <div className="error-message">{error}</div>;
  if (!health) return null;

  const cards = [
    { label: "Active Sessions", value: health.activeSessions },
    { label: "Completed Sessions", value: health.completedSessions },
    { label: "Stale Sessions", value: health.staleSessions },
    { label: "Total Tasks", value: health.totalTasks },
    { label: "Active Tasks", value: health.activeTasks },
  ];

  return (
    <div>
      <h2>Dashboard</h2>
      <div className="health-grid">
        {cards.map((c) => (
          <div key={c.label} className="health-card">
            <div className="label">{c.label}</div>
            <div className="value">{c.value}</div>
          </div>
        ))}
      </div>
      <div className="health-meta">
        Last updated: {new Date(health.timestamp).toLocaleString()}
      </div>
    </div>
  );
}
