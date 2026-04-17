import { useEffect, useState, useCallback } from "react";
import { listSessions, listPrompts } from "../api";
import type { Session, Prompt } from "../api";
import {
  BarChart,
  Bar,
  PieChart,
  Pie,
  Cell,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from "recharts";

const COLORS = ["#58a6ff", "#3fb950", "#d29922", "#f85149", "#bc8cff", "#f0883e", "#a5d6ff", "#7ee787"];

interface ChartData {
  name: string;
  value: number;
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="chart-card">
      <h3 className="chart-card-title">{title}</h3>
      {children}
    </div>
  );
}

function DonutChart({ data }: { data: ChartData[] }) {
  const total = data.reduce((sum, d) => sum + d.value, 0);
  if (total === 0) return <div className="chart-empty">No data</div>;

  return (
    <ResponsiveContainer width="100%" height={240}>
      <PieChart>
        <Pie
          data={data}
          cx="50%"
          cy="50%"
          innerRadius={55}
          outerRadius={85}
          paddingAngle={2}
          dataKey="value"
          label={({ name, value }) => `${name} (${value})`}
          labelLine={false}
        >
          {data.map((_entry, i) => (
            <Cell key={i} fill={COLORS[i % COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={{ background: "#161b22", border: "1px solid #30363d", borderRadius: 6, color: "#e6edf3" }}
        />
      </PieChart>
    </ResponsiveContainer>
  );
}

function HorizontalBarChart({ data, color }: { data: ChartData[]; color?: string }) {
  if (data.length === 0) return <div className="chart-empty">No data</div>;

  const sorted = [...data].sort((a, b) => b.value - a.value).slice(0, 10);

  return (
    <ResponsiveContainer width="100%" height={Math.max(200, sorted.length * 36)}>
      <BarChart data={sorted} layout="vertical" margin={{ left: 0, right: 20, top: 5, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#30363d" horizontal={false} />
        <XAxis type="number" tick={{ fill: "#8b949e", fontSize: 12 }} axisLine={{ stroke: "#30363d" }} />
        <YAxis
          type="category"
          dataKey="name"
          width={140}
          tick={{ fill: "#e6edf3", fontSize: 12 }}
          axisLine={{ stroke: "#30363d" }}
        />
        <Tooltip
          contentStyle={{ background: "#161b22", border: "1px solid #30363d", borderRadius: 6, color: "#e6edf3" }}
        />
        <Bar dataKey="value" fill={color || COLORS[0]} radius={[0, 4, 4, 0]} />
      </BarChart>
    </ResponsiveContainer>
  );
}

function StackedBarChart({ data, keys, colors }: { data: Record<string, unknown>[]; keys: string[]; colors: string[] }) {
  if (data.length === 0) return <div className="chart-empty">No data</div>;

  return (
    <ResponsiveContainer width="100%" height={Math.max(200, data.length * 36)}>
      <BarChart data={data} layout="vertical" margin={{ left: 0, right: 20, top: 5, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#30363d" horizontal={false} />
        <XAxis type="number" tick={{ fill: "#8b949e", fontSize: 12 }} axisLine={{ stroke: "#30363d" }} />
        <YAxis
          type="category"
          dataKey="name"
          width={140}
          tick={{ fill: "#e6edf3", fontSize: 12 }}
          axisLine={{ stroke: "#30363d" }}
        />
        <Tooltip
          contentStyle={{ background: "#161b22", border: "1px solid #30363d", borderRadius: 6, color: "#e6edf3" }}
        />
        <Legend wrapperStyle={{ color: "#8b949e", fontSize: 12 }} />
        {keys.map((key, i) => (
          <Bar key={key} dataKey={key} stackId="stack" fill={colors[i % colors.length]} radius={i === keys.length - 1 ? [0, 4, 4, 0] : undefined} />
        ))}
      </BarChart>
    </ResponsiveContainer>
  );
}

export function ChartsDashboard() {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [prompts, setPrompts] = useState<Prompt[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    try {
      const [sessResult, promptResult] = await Promise.all([
        listSessions({ pageSize: 100 }),
        listPrompts({ pageSize: 100 }),
      ]);
      setSessions(sessResult.items);
      setPrompts(promptResult.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load chart data");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 60_000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading charts...</div>;
  if (error) return <div className="error-message">{error}</div>;

  // Session status breakdown
  const sessionsByStatus = aggregate(sessions, (s) => s.status);

  // Sessions by machine
  const sessionsByMachine = aggregate(sessions, (s) => s.machineId);

  // Sessions by repository
  const sessionsByRepo = aggregate(
    sessions.filter((s) => s.repository),
    (s) => s.repository || "unknown"
  );

  // Prompt status breakdown
  const promptsByStatus = aggregate(prompts, (p) => p.status);

  // Prompts per machine (via session mapping)
  const sessionMachineMap = new Map(sessions.map((s) => [s.id, s.machineId]));
  const promptsByMachine = aggregate(prompts, (p) => sessionMachineMap.get(p.sessionId) || "unknown");

  // Prompts per repository
  const sessionRepoMap = new Map(sessions.map((s) => [s.id, s.repository || ""]));
  const promptsByRepo = aggregate(
    prompts.filter((p) => sessionRepoMap.get(p.sessionId)),
    (p) => sessionRepoMap.get(p.sessionId) || "unknown"
  );

  // Prompts per session (top 10)
  const promptsPerSession = aggregate(prompts, (p) => {
    const machine = sessionMachineMap.get(p.sessionId) || "?";
    return `${machine}/${p.sessionId.slice(0, 8)}`;
  });

  // Prompt status by machine (stacked)
  const promptStatusByMachine = buildStackedData(
    prompts,
    (p) => sessionMachineMap.get(p.sessionId) || "unknown",
    (p) => p.status
  );
  const promptStatuses = [...new Set(prompts.map((p) => p.status))];
  const statusColors: Record<string, string> = {
    started: "#d29922",
    done: "#3fb950",
    failed: "#f85149",
  };

  return (
    <div>
      <h2>Analytics</h2>

      <div className="charts-row">
        <ChartCard title="Sessions by Status">
          <DonutChart data={sessionsByStatus} />
        </ChartCard>
        <ChartCard title="Prompts by Status">
          <DonutChart data={promptsByStatus} />
        </ChartCard>
      </div>

      <div className="charts-row">
        <ChartCard title="Sessions by Machine">
          <HorizontalBarChart data={sessionsByMachine} color="#58a6ff" />
        </ChartCard>
        <ChartCard title="Prompts by Machine">
          <HorizontalBarChart data={promptsByMachine} color="#bc8cff" />
        </ChartCard>
      </div>

      {sessionsByRepo.length > 0 && (
        <div className="charts-row">
          <ChartCard title="Sessions by Repository">
            <HorizontalBarChart data={sessionsByRepo} color="#3fb950" />
          </ChartCard>
          <ChartCard title="Prompts by Repository">
            <HorizontalBarChart data={promptsByRepo} color="#d29922" />
          </ChartCard>
        </div>
      )}

      <div className="charts-row">
        <ChartCard title="Prompts per Session (Top 10)">
          <HorizontalBarChart data={promptsPerSession} color="#f0883e" />
        </ChartCard>
        <ChartCard title="Prompt Status by Machine">
          <StackedBarChart
            data={promptStatusByMachine}
            keys={promptStatuses}
            colors={promptStatuses.map((s) => statusColors[s] || "#58a6ff")}
          />
        </ChartCard>
      </div>
    </div>
  );
}

function aggregate<T>(items: T[], keyFn: (item: T) => string): ChartData[] {
  const counts = new Map<string, number>();
  for (const item of items) {
    const key = keyFn(item);
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  return Array.from(counts.entries())
    .map(([name, value]) => ({ name, value }))
    .sort((a, b) => b.value - a.value);
}

function buildStackedData<T>(
  items: T[],
  groupFn: (item: T) => string,
  categoryFn: (item: T) => string
): Record<string, unknown>[] {
  const groups = new Map<string, Map<string, number>>();
  for (const item of items) {
    const group = groupFn(item);
    const cat = categoryFn(item);
    if (!groups.has(group)) groups.set(group, new Map());
    const catMap = groups.get(group)!;
    catMap.set(cat, (catMap.get(cat) || 0) + 1);
  }
  return Array.from(groups.entries()).map(([name, catMap]) => {
    const row: Record<string, unknown> = { name };
    for (const [cat, count] of catMap) {
      row[cat] = count;
    }
    return row;
  });
}
