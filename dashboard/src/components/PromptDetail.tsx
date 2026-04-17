import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { getPrompt, getPromptLogs } from "../api";
import type { Prompt, PromptLog } from "../api";

const PROMPT_PREVIEW_LENGTH = 200;

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge badge-${status}`}>{status}</span>;
}

function formatDate(iso?: string) {
  return iso ? new Date(iso).toLocaleString() : "-";
}

function PromptText({ text }: { text: string }) {
  const [expanded, setExpanded] = useState(false);
  const needsTruncation = text.length > PROMPT_PREVIEW_LENGTH;

  return (
    <div className="prompt-text-container">
      <div className={`prompt-text-content ${expanded ? "expanded" : ""}`}>
        {expanded || !needsTruncation ? text : text.slice(0, PROMPT_PREVIEW_LENGTH) + "..."}
      </div>
      {needsTruncation && (
        <button className="prompt-text-toggle" onClick={() => setExpanded(!expanded)}>
          {expanded ? "Show less" : `Show full prompt (${text.length} chars)`}
        </button>
      )}
    </div>
  );
}

export function PromptDetail() {
  const { sessionId, id } = useParams<{ sessionId: string; id: string }>();
  const [prompt, setPrompt] = useState<Prompt | null>(null);
  const [logs, setLogs] = useState<PromptLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId || !id) return;

    async function load() {
      try {
        const [promptData, logsData] = await Promise.all([
          getPrompt(sessionId!, id!),
          getPromptLogs(sessionId!, id!),
        ]);
        setPrompt(promptData);
        setLogs(logsData.items.sort((a, b) => a.timestamp.localeCompare(b.timestamp)));
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Failed to load prompt");
      } finally {
        setLoading(false);
      }
    }

    load();
  }, [sessionId, id]);

  if (loading) return <div className="loading">Loading prompt...</div>;
  if (error) return <div className="error-message">{error}</div>;
  if (!prompt) return <div className="empty-state">Prompt not found.</div>;

  const fields = [
    { label: "Prompt ID", value: prompt.id },
    { label: "Session ID", value: prompt.sessionId },
    { label: "Status", value: prompt.status, badge: true },
    { label: "Title", value: prompt.title },
    ...(prompt.cwd ? [{ label: "Working Directory", value: prompt.cwd }] : []),
    { label: "Source", value: prompt.source },
    { label: "Created", value: formatDate(prompt.createdAt) },
    { label: "Updated", value: formatDate(prompt.updatedAt) },
    ...(prompt.hookTimestamp ? [{ label: "Hook Timestamp", value: formatDate(prompt.hookTimestamp) }] : []),
    { label: "User ID", value: prompt.userId },
    { label: "Created By", value: prompt.createdBy },
  ];

  return (
    <div>
      <Link to="/sessions" className="back-link">
        &larr; Back to Sessions
      </Link>

      <div className="detail-card">
        <h2>Prompt Details</h2>
        <div className="detail-grid">
          {fields.map((f) => (
            <div key={f.label} className="detail-field">
              <div className="label">{f.label}</div>
              <div className="value">
                {"badge" in f && f.badge ? <StatusBadge status={String(f.value)} /> : f.value}
              </div>
            </div>
          ))}
          {prompt.result && (
            <div className="detail-field">
              <div className="label">Result</div>
              <div className="value">{prompt.result}</div>
            </div>
          )}
          {prompt.errorMessage && (
            <div className="detail-field">
              <div className="label">Error</div>
              <div className="value" style={{ color: "var(--red)" }}>
                {prompt.errorMessage}
              </div>
            </div>
          )}
        </div>
      </div>

      {prompt.promptText && (
        <div className="detail-card">
          <h2>Prompt Text</h2>
          <PromptText text={prompt.promptText} />
        </div>
      )}

      <div className="detail-card">
        <h2>Prompt Logs ({logs.length})</h2>
        {logs.length === 0 ? (
          <div className="empty-state">No log entries.</div>
        ) : (
          logs.map((log) => (
            <div key={log.id} className="log-entry">
              <span className="log-time">{formatDate(log.timestamp)}</span>
              <StatusBadge status={log.logType} />
              {log.agentName && <span className="log-agent">[{log.agentName}]</span>}
              <span className="log-message">{log.message}</span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
