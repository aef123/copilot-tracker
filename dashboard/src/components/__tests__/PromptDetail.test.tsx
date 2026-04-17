import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { PromptDetail } from "../PromptDetail";
import type { Prompt, PromptLog } from "../../api";

vi.mock("../../api", () => ({
  getPrompt: vi.fn(),
  getPromptLogs: vi.fn(),
}));

import { getPrompt, getPromptLogs } from "../../api";
const mockGetPrompt = vi.mocked(getPrompt);
const mockGetPromptLogs = vi.mocked(getPromptLogs);

function renderPromptDetail(sessionId = "sess-1", id = "prompt-1") {
  return render(
    <MemoryRouter initialEntries={[`/prompts/${sessionId}/${id}`]}>
      <Routes>
        <Route path="/prompts/:sessionId/:id" element={<PromptDetail />} />
      </Routes>
    </MemoryRouter>
  );
}

const basePrompt: Prompt = {
  id: "prompt-1",
  sessionId: "sess-1",
  queueName: "default",
  title: "Implement feature",
  status: "done",
  result: "Feature complete",
  source: "prompt",
  createdAt: "2025-01-15T10:00:00Z",
  updatedAt: "2025-01-15T10:01:00Z",
  userId: "user-1",
  createdBy: "copilot",
};

const baseLogs: PromptLog[] = [
  {
    id: "log-1",
    promptId: "prompt-1",
    logType: "progress",
    message: "Starting work",
    timestamp: "2025-01-15T10:00:10Z",
  },
  {
    id: "log-2",
    promptId: "prompt-1",
    logType: "status_change",
    message: "Work complete",
    timestamp: "2025-01-15T10:01:00Z",
  },
];

describe("PromptDetail", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockGetPrompt.mockReturnValue(new Promise(() => {}));
    mockGetPromptLogs.mockReturnValue(new Promise(() => {}));

    renderPromptDetail();
    expect(screen.getByText("Loading prompt...")).toBeInTheDocument();
  });

  it("renders prompt details when data loads", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("Prompt Details")).toBeInTheDocument();
    expect(screen.getByText("prompt-1")).toBeInTheDocument();
    expect(screen.getByText("done")).toBeInTheDocument();
  });

  it("renders error state on API failure", async () => {
    mockGetPrompt.mockRejectedValue(new Error("Server error"));
    mockGetPromptLogs.mockRejectedValue(new Error("Server error"));

    renderPromptDetail();

    expect(await screen.findByText("Server error")).toBeInTheDocument();
  });

  it("shows result field when prompt has result", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("Feature complete")).toBeInTheDocument();
    expect(screen.getByText("Result")).toBeInTheDocument();
  });

  it("shows error field when prompt has errorMessage", async () => {
    const failedPrompt: Prompt = {
      ...basePrompt,
      status: "failed",
      result: undefined,
      errorMessage: "Compilation failed",
    };
    mockGetPrompt.mockResolvedValue(failedPrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("Compilation failed")).toBeInTheDocument();
    expect(screen.getByText("Error")).toBeInTheDocument();
  });

  it("does not show result/error fields when absent", async () => {
    const noResultPrompt: Prompt = {
      ...basePrompt,
      result: undefined,
      errorMessage: undefined,
    };
    mockGetPrompt.mockResolvedValue(noResultPrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    await screen.findByText("Prompt Details");
    expect(screen.queryByText("Result")).not.toBeInTheDocument();
    expect(screen.queryByText("Error")).not.toBeInTheDocument();
  });

  it("shows empty log state when no logs", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("No log entries.")).toBeInTheDocument();
    expect(screen.getByText("Prompt Logs (0)")).toBeInTheDocument();
  });

  it("renders log entries sorted by timestamp", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({
      items: [baseLogs[1], baseLogs[0]],
      hasMore: false,
    });

    renderPromptDetail();

    expect(await screen.findByText("Starting work")).toBeInTheDocument();
    expect(screen.getByText("Work complete")).toBeInTheDocument();
    expect(screen.getByText("Prompt Logs (2)")).toBeInTheDocument();
  });

  it("renders back link to sessions", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    const backLink = await screen.findByText(/Back to Sessions/);
    expect(backLink.closest("a")).toHaveAttribute("href", "/sessions");
  });

  it("handles non-Error thrown from API", async () => {
    mockGetPrompt.mockRejectedValue("raw string");
    mockGetPromptLogs.mockRejectedValue("raw string");

    renderPromptDetail();

    expect(await screen.findByText("Failed to load prompt")).toBeInTheDocument();
  });

  it("sorts logs chronologically (earliest first)", async () => {
    const unorderedLogs: PromptLog[] = [
      {
        id: "log-3",
        promptId: "prompt-1",
        logType: "progress",
        message: "Third entry",
        timestamp: "2025-01-15T10:02:00Z",
      },
      {
        id: "log-1",
        promptId: "prompt-1",
        logType: "status_change",
        message: "First entry",
        timestamp: "2025-01-15T10:00:00Z",
      },
      {
        id: "log-2",
        promptId: "prompt-1",
        logType: "progress",
        message: "Second entry",
        timestamp: "2025-01-15T10:01:00Z",
      },
    ];

    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: unorderedLogs, hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("First entry")).toBeInTheDocument();
    expect(screen.getByText("Second entry")).toBeInTheDocument();
    expect(screen.getByText("Third entry")).toBeInTheDocument();

    const logEntries = document.querySelectorAll(".log-entry");
    const messages = Array.from(logEntries).map((el) =>
      el.querySelector(".log-message")?.textContent
    );
    expect(messages).toEqual(["First entry", "Second entry", "Third entry"]);
  });

  it("renders log type badges for each log entry", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({
      items: [
        { id: "l1", promptId: "prompt-1", logType: "progress", message: "Msg1", timestamp: "2025-01-15T10:00:00Z" },
        { id: "l2", promptId: "prompt-1", logType: "error", message: "Msg2", timestamp: "2025-01-15T10:01:00Z" },
      ],
      hasMore: false,
    });

    renderPromptDetail();

    expect(await screen.findByText("progress")).toBeInTheDocument();
    expect(screen.getByText("error")).toBeInTheDocument();
  });

  it("renders agent name when present in log", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({
      items: [
        {
          id: "l1",
          promptId: "prompt-1",
          logType: "subagent_start",
          message: "Agent started",
          timestamp: "2025-01-15T10:00:00Z",
          agentName: "explore-agent",
        },
      ],
      hasMore: false,
    });

    renderPromptDetail();

    expect(await screen.findByText("[explore-agent]")).toBeInTheDocument();
  });

  it("displays prompt text when present", async () => {
    const promptWithText: Prompt = {
      ...basePrompt,
      promptText: "Add unit tests for auth module",
    };
    mockGetPrompt.mockResolvedValue(promptWithText);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    expect(await screen.findByText("Add unit tests for auth module")).toBeInTheDocument();
    expect(screen.getByText("Prompt Text")).toBeInTheDocument();
  });

  it("displays all prompt metadata fields", async () => {
    mockGetPrompt.mockResolvedValue(basePrompt);
    mockGetPromptLogs.mockResolvedValue({ items: [], hasMore: false });

    renderPromptDetail();

    await screen.findByText("Prompt Details");
    expect(screen.getByText("Prompt ID")).toBeInTheDocument();
    expect(screen.getByText("Session ID")).toBeInTheDocument();
    expect(screen.getByText("Status")).toBeInTheDocument();
  });
});
