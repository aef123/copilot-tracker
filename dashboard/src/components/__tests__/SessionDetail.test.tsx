import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { SessionDetail } from "../SessionDetail";
import type { Session, Prompt } from "../../api";

vi.mock("../../api", () => ({
  getSession: vi.fn(),
  listPrompts: vi.fn(),
}));

import { getSession, listPrompts } from "../../api";
const mockGetSession = vi.mocked(getSession);
const mockListPrompts = vi.mocked(listPrompts);

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

const basePrompt: Prompt = {
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
    mockListPrompts.mockReturnValue(new Promise(() => {}));

    renderSessionDetail();
    expect(screen.getByText("Loading session...")).toBeInTheDocument();
  });

  it("renders session details when data loads", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("Session Details")).toBeInTheDocument();
    expect(screen.getByText("sess-1")).toBeInTheDocument();
    expect(screen.getByText("machine-1")).toBeInTheDocument();
    expect(screen.getByText("org/repo")).toBeInTheDocument();
    expect(screen.getByText("main")).toBeInTheDocument();
    expect(screen.getByText("Idle")).toBeInTheDocument();
  });

  it("renders error state on API failure", async () => {
    mockGetSession.mockRejectedValue(new Error("Network timeout"));
    mockListPrompts.mockRejectedValue(new Error("Network timeout"));

    renderSessionDetail();

    expect(await screen.findByText("Network timeout")).toBeInTheDocument();
  });

  it("shows empty task state when session has no tasks", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("No prompts for this session.")).toBeInTheDocument();
    expect(screen.getByText("Prompts (0)")).toBeInTheDocument();
  });

  it("renders prompts returned by the API", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({
      items: [
        basePrompt,
        { ...basePrompt, id: "task-2", title: "Deploy app" },
      ],
      hasMore: false,
    });

    renderSessionDetail();

    expect(await screen.findByText("Run tests")).toBeInTheDocument();
    expect(screen.getByText("Deploy app")).toBeInTheDocument();
    expect(screen.getByText("Prompts (2)")).toBeInTheDocument();
  });

  it("displays task result in the table", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [basePrompt], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("All passed")).toBeInTheDocument();
  });

  it("displays task error message for failed tasks", async () => {
    const failedPrompt: Prompt = {
      ...basePrompt,
      status: "failed",
      result: undefined,
      errorMessage: "Compilation error",
    };
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [failedPrompt], hasMore: false });

    renderSessionDetail();

    expect(await screen.findByText("Compilation error")).toBeInTheDocument();
  });

  it("renders back link to sessions", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    const backLink = await screen.findByText(/Back to Sessions/);
    expect(backLink).toBeInTheDocument();
    expect(backLink.closest("a")).toHaveAttribute("href", "/sessions");
  });

  it("shows session summary when present", async () => {
    mockGetSession.mockResolvedValue({ ...baseSession, summary: "Refactored auth module" });
    mockListPrompts.mockResolvedValue({ items: [], hasMore: false });

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
    mockListPrompts.mockResolvedValue({ items: [], hasMore: false });

    renderSessionDetail();

    await screen.findByText("Session Details");
    const dashes = screen.getAllByText("-");
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  it("handles non-Error thrown from API", async () => {
    mockGetSession.mockRejectedValue("string error");
    mockListPrompts.mockRejectedValue("string error");

    renderSessionDetail();

    expect(await screen.findByText("Failed to load session")).toBeInTheDocument();
  });

  it("calls listPrompts with the session id for server-side filtering", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [basePrompt], hasMore: false });

    renderSessionDetail();

    await screen.findByText("Run tests");

    // Import and check that listPrompts was called with sessionId
    const { listPrompts: actualListPrompts } = await import("../../api");
    expect(vi.mocked(actualListPrompts)).toHaveBeenCalledWith({ sessionId: "sess-1" });
  });

  it("displays all task table columns", async () => {
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [basePrompt], hasMore: false });

    renderSessionDetail();

    await screen.findByText("Run tests");
    const headers = document.querySelectorAll("th");
    const headerTexts = Array.from(headers).map((h) => h.textContent);
    expect(headerTexts).toContain("Status");
    expect(headerTexts).toContain("Prompt");
    expect(headerTexts).toContain("Created");
    expect(headerTexts).toContain("Result / Error");
  });

  it("shows dash for task result/error when both absent", async () => {
    const noResultPrompt: Prompt = {
      ...basePrompt,
      result: undefined,
      errorMessage: undefined,
    };
    mockGetSession.mockResolvedValue(baseSession);
    mockListPrompts.mockResolvedValue({ items: [noResultPrompt], hasMore: false });

    renderSessionDetail();

    await screen.findByText("Run tests");
    // The table cell shows "-" when neither result nor errorMessage exists
    const cells = document.querySelectorAll("td");
    const lastTd = cells[cells.length - 1];
    expect(lastTd.textContent).toBe("-");
  });
});
