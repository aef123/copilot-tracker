import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { TaskDetail } from "../TaskDetail";
import type { TrackerTask, TaskLog, PagedResult } from "../../api";

vi.mock("../../api", () => ({
  getTask: vi.fn(),
  getTaskLogs: vi.fn(),
}));

import { getTask, getTaskLogs } from "../../api";
const mockGetTask = vi.mocked(getTask);
const mockGetTaskLogs = vi.mocked(getTaskLogs);

function renderTaskDetail(queueName = "default", id = "task-1") {
  return render(
    <MemoryRouter initialEntries={[`/tasks/${queueName}/${id}`]}>
      <Routes>
        <Route path="/tasks/:queueName/:id" element={<TaskDetail />} />
      </Routes>
    </MemoryRouter>
  );
}

const baseTask: TrackerTask = {
  id: "task-1",
  sessionId: "sess-1",
  queueName: "default",
  title: "Build project",
  status: "done",
  result: "Build succeeded",
  source: "prompt",
  createdAt: "2025-01-15T10:00:00Z",
  updatedAt: "2025-01-15T10:01:00Z",
  userId: "user-1",
  createdBy: "copilot",
};

const baseLogs: TaskLog[] = [
  {
    id: "log-1",
    taskId: "task-1",
    logType: "progress",
    message: "Starting build",
    timestamp: "2025-01-15T10:00:10Z",
  },
  {
    id: "log-2",
    taskId: "task-1",
    logType: "status_change",
    message: "Build complete",
    timestamp: "2025-01-15T10:01:00Z",
  },
];

describe("TaskDetail", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockGetTask.mockReturnValue(new Promise(() => {}));
    mockGetTaskLogs.mockReturnValue(new Promise(() => {}));

    renderTaskDetail();
    expect(screen.getByText("Loading task...")).toBeInTheDocument();
  });

  it("renders task details when data loads", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    expect(await screen.findByText("Task Details")).toBeInTheDocument();
    expect(screen.getByText("task-1")).toBeInTheDocument();
    expect(screen.getByText("Build project")).toBeInTheDocument();
    expect(screen.getByText("done")).toBeInTheDocument();
    expect(screen.getByText("default")).toBeInTheDocument();
    expect(screen.getByText("prompt")).toBeInTheDocument();
  });

  it("renders error state on API failure", async () => {
    mockGetTask.mockRejectedValue(new Error("Server error"));
    mockGetTaskLogs.mockRejectedValue(new Error("Server error"));

    renderTaskDetail();

    expect(await screen.findByText("Server error")).toBeInTheDocument();
  });

  it("shows result field when task has result", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    expect(await screen.findByText("Build succeeded")).toBeInTheDocument();
    expect(screen.getByText("Result")).toBeInTheDocument();
  });

  it("shows error field when task has errorMessage", async () => {
    const failedTask: TrackerTask = {
      ...baseTask,
      status: "failed",
      result: undefined,
      errorMessage: "Compilation failed",
    };
    mockGetTask.mockResolvedValue(failedTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    expect(await screen.findByText("Compilation failed")).toBeInTheDocument();
    expect(screen.getByText("Error")).toBeInTheDocument();
  });

  it("does not show result/error fields when absent", async () => {
    const noResultTask: TrackerTask = {
      ...baseTask,
      result: undefined,
      errorMessage: undefined,
    };
    mockGetTask.mockResolvedValue(noResultTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    await screen.findByText("Task Details");
    expect(screen.queryByText("Result")).not.toBeInTheDocument();
    expect(screen.queryByText("Error")).not.toBeInTheDocument();
  });

  it("shows empty log state when no logs", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    expect(await screen.findByText("No log entries.")).toBeInTheDocument();
    expect(screen.getByText("Logs (0)")).toBeInTheDocument();
  });

  it("renders log entries sorted by timestamp", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({
      items: [baseLogs[1], baseLogs[0]],
      hasMore: false,
    });

    renderTaskDetail();

    expect(await screen.findByText("Starting build")).toBeInTheDocument();
    expect(screen.getByText("Build complete")).toBeInTheDocument();
    expect(screen.getByText("Logs (2)")).toBeInTheDocument();
  });

  it("renders back link to sessions", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    const backLink = await screen.findByText(/Back to Sessions/);
    expect(backLink.closest("a")).toHaveAttribute("href", "/sessions");
  });

  it("handles non-Error thrown from API", async () => {
    mockGetTask.mockRejectedValue("raw string");
    mockGetTaskLogs.mockRejectedValue("raw string");

    renderTaskDetail();

    expect(await screen.findByText("Failed to load task")).toBeInTheDocument();
  });

  it("sorts logs chronologically (earliest first)", async () => {
    const unorderedLogs: TaskLog[] = [
      {
        id: "log-3",
        taskId: "task-1",
        logType: "progress",
        message: "Third entry",
        timestamp: "2025-01-15T10:02:00Z",
      },
      {
        id: "log-1",
        taskId: "task-1",
        logType: "status_change",
        message: "First entry",
        timestamp: "2025-01-15T10:00:00Z",
      },
      {
        id: "log-2",
        taskId: "task-1",
        logType: "progress",
        message: "Second entry",
        timestamp: "2025-01-15T10:01:00Z",
      },
    ];

    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: unorderedLogs, hasMore: false });

    renderTaskDetail();

    const logMessages = await screen.findAllByClassName
      ? undefined
      : undefined;
    // Verify all three appear
    expect(await screen.findByText("First entry")).toBeInTheDocument();
    expect(screen.getByText("Second entry")).toBeInTheDocument();
    expect(screen.getByText("Third entry")).toBeInTheDocument();

    // Verify ordering: First should appear before Second, Second before Third
    const logEntries = document.querySelectorAll(".log-entry");
    const messages = Array.from(logEntries).map((el) =>
      el.querySelector(".log-message")?.textContent
    );
    expect(messages).toEqual(["First entry", "Second entry", "Third entry"]);
  });

  it("renders log type badges for each log entry", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({
      items: [
        { id: "l1", taskId: "task-1", logType: "progress", message: "Msg1", timestamp: "2025-01-15T10:00:00Z" },
        { id: "l2", taskId: "task-1", logType: "error", message: "Msg2", timestamp: "2025-01-15T10:01:00Z" },
      ],
      hasMore: false,
    });

    renderTaskDetail();

    expect(await screen.findByText("progress")).toBeInTheDocument();
    expect(screen.getByText("error")).toBeInTheDocument();
  });

  it("displays correct log count", async () => {
    const manyLogs: TaskLog[] = Array.from({ length: 5 }, (_, i) => ({
      id: `log-${i}`,
      taskId: "task-1",
      logType: "progress" as const,
      message: `Log message ${i}`,
      timestamp: `2025-01-15T10:0${i}:00Z`,
    }));

    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: manyLogs, hasMore: false });

    renderTaskDetail();

    expect(await screen.findByText("Logs (5)")).toBeInTheDocument();
  });

  it("displays all task metadata fields", async () => {
    mockGetTask.mockResolvedValue(baseTask);
    mockGetTaskLogs.mockResolvedValue({ items: [], hasMore: false });

    renderTaskDetail();

    await screen.findByText("Task Details");
    expect(screen.getByText("Task ID")).toBeInTheDocument();
    expect(screen.getByText("Session ID")).toBeInTheDocument();
    expect(screen.getByText("Queue")).toBeInTheDocument();
    expect(screen.getByText("Status")).toBeInTheDocument();
    expect(screen.getByText("Title")).toBeInTheDocument();
    expect(screen.getByText("Source")).toBeInTheDocument();
  });
});
