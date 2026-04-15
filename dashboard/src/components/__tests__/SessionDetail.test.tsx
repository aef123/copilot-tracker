import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { SessionDetail } from "../SessionDetail";
import type { Session, TrackerTask, PagedResult } from "../../api";

vi.mock("../../api", () => ({
  getSession: vi.fn(),
  listTasks: vi.fn(),
}));

import { getSession, listTasks } from "../../api";
const mockGetSession = vi.mocked(getSession);
const mockListTasks = vi.mocked(listTasks);

function renderSessionDetail(machineId = "machine-1", id = "sess-1") {
  return render(
    <MemoryRouter initialEntries={[`/sessions/${machineId}/${id}`]}>
      <Routes>
        <Route path="/sessions/:machineId/:id" element={<SessionDetail />} />
      </Routes>
    </MemoryRouter>
  );
}

const baseSession: Session = {
  id: "sess-1",
  machineId: "machine-1",
  repository: "org/repo",
  branch: "main",
  status: "active",
  createdAt: "2025-01-15T10:00:00Z",
  updatedAt: "2025-01-15T10:05:00Z",
  lastHeartbeat: "2025-01-15T10:04:00Z",
  userId: "user-1",
  createdBy: "copilot",
};

const baseTask: TrackerTask = {
  id: "task-1",
  sessionId: "sess-1",
  queueName: "default",
  title: "Run tests",
  status: "done",
  result: "All passed",
  source: "prompt",
  createdAt: "2025-01-15T10:01:00Z",
  updatedAt: "2025-01-15T10:02:00Z",
  userId: "user-1",
  createdBy: "copilot",
};

describe("SessionDetail", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockGetSession.mockReturnValue(new Promise(() => {}));
    mockListTasks.mockReturnValue(new Promise(() => {}));

    renderSessionDetail();
    expect(screen.getByText("Loading session...")).toBeInTheDocument();
  });

  it("renders session details when data loads", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("Session Details")).toBeInTheDocument();
    expect(screen.getByText("sess-1")).toBeInTheDocument();
    expect(screen.getByText("machine-1")).toBeInTheDocument();
    expect(screen.getByText("org/repo")).toBeInTheDocument();
    expect(screen.getByText("main")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
  });

  it("renders error state on API failure", async () => {
    mockGetSession.mockRejectedValue(new Error("Network timeout"));
    mockListTasks.mockRejectedValue(new Error("Network timeout"));

    renderSessionDetail();

    expect(await screen.findByText("Network timeout")).toBeInTheDocument();
  });

  it("shows empty task state when session has no tasks", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("No tasks for this session.")).toBeInTheDocument();
    expect(screen.getByText("Tasks (0)")).toBeInTheDocument();
  });

  it("renders tasks that belong to this session", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({
      items: [
        baseTask,
        { ...baseTask, id: "task-other", sessionId: "other-session", title: "Other task" },
      ],
      hasMore: false,
    });

    renderSessionDetail();

    expect(await screen.findByText("Run tests")).toBeInTheDocument();
    expect(screen.queryByText("Other task")).not.toBeInTheDocument();
    expect(screen.getByText("Tasks (1)")).toBeInTheDocument();
  });

  it("displays task result in the table", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({ items: [baseTask], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("All passed")).toBeInTheDocument();
  });

  it("displays task error message for failed tasks", async () => {
    const failedTask: TrackerTask = {
      ...baseTask,
      status: "failed",
      result: undefined,
      errorMessage: "Compilation error",
    };
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({ items: [failedTask], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("Compilation error")).toBeInTheDocument();
  });

  it("renders back link to sessions", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListTasks.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    const backLink = await screen.findByText(/Back to Sessions/);
    expect(backLink).toBeInTheDocument();
    expect(backLink.closest("a")).toHaveAttribute("href", "/sessions");
  });

  it("shows session summary when present", async () => {
    mockGetSession.mockResolvedValue({ ...baseSession, summary: "Refactored auth module" });
    mockListTasks.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("Refactored auth module")).toBeInTheDocument();
  });

  it("shows dash for optional fields that are missing", async () => {
    const sessionNoOptionals: Session = {
      ...baseSession,
      repository: undefined,
      branch: undefined,
      completedAt: undefined,
    };
    mockGetSession.mockResolvedValue(sessionNoOptionals);
    mockListTasks.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    await screen.findByText("Session Details");
    const dashes = screen.getAllByText("-");
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  it("handles non-Error thrown from API", async () => {
    mockGetSession.mockRejectedValue("string error");
    mockListTasks.mockRejectedValue("string error");

    renderSessionDetail();

    expect(await screen.findByText("Failed to load session")).toBeInTheDocument();
  });
});
