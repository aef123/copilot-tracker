import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { SessionList } from "../SessionList";
import type { PagedResult, Session } from "../../api";

vi.mock("../../api", () => ({
  listSessions: vi.fn(),
}));

import { listSessions } from "../../api";
const mockListSessions = vi.mocked(listSessions);

function renderWithRouter() {
  return render(
    <MemoryRouter>
      <SessionList />
    </MemoryRouter>
  );
}

describe("SessionList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders session rows", async () => {
    const result: PagedResult<Session> = {
      items: [
        {
          id: "sess-1",
          machineId: "machine-a",
          repository: "org/repo",
          branch: "main",
          status: "active",
          createdAt: "2025-01-15T10:00:00Z",
          updatedAt: "2025-01-15T10:05:00Z",
          lastHeartbeat: "2025-01-15T10:04:00Z",
          userId: "user-1",
          createdBy: "copilot",
        },
      ],
      hasMore: false,
    };
    mockListSessions.mockResolvedValue(result);

    renderWithRouter();

    expect(await screen.findByText("machine-a")).toBeInTheDocument();
    expect(screen.getByText("repo")).toBeInTheDocument();
    expect(screen.getByText("main")).toBeInTheDocument();
    expect(screen.getByText("Idle")).toBeInTheDocument();
  });

  it("shows empty state when no sessions", async () => {
    mockListSessions.mockResolvedValue({ items: [], hasMore: false });

    renderWithRouter();

    expect(await screen.findByText("No sessions found.")).toBeInTheDocument();
  });

  it("shows Load More button when hasMore is true", async () => {
    mockListSessions.mockResolvedValue({
      items: [
        {
          id: "s1",
          machineId: "m1",
          status: "active",
          createdAt: "2025-01-15T10:00:00Z",
          updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z",
          userId: "u1",
          createdBy: "copilot",
        },
      ],
      hasMore: true,
      continuationToken: "token123",
    });

    renderWithRouter();

    expect(await screen.findByText("Load More")).toBeInTheDocument();
  });

  it("hides Load More button when hasMore is false", async () => {
    mockListSessions.mockResolvedValue({
      items: [
        {
          id: "s1",
          machineId: "m1",
          status: "active",
          createdAt: "2025-01-15T10:00:00Z",
          updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z",
          userId: "u1",
          createdBy: "copilot",
        },
      ],
      hasMore: false,
    });

    renderWithRouter();

    await screen.findByText("m1");
    expect(screen.queryByText("Load More")).not.toBeInTheDocument();
  });

  it("shows loading state initially", () => {
    mockListSessions.mockReturnValue(new Promise(() => {}));
    renderWithRouter();
    expect(screen.getByText("Loading sessions...")).toBeInTheDocument();
  });

  it("renders error state on failure", async () => {
    mockListSessions.mockRejectedValue(new Error("Server down"));

    renderWithRouter();

    expect(await screen.findByText("Server down")).toBeInTheDocument();
  });

  it("handles non-Error thrown from API", async () => {
    mockListSessions.mockRejectedValue("string error");

    renderWithRouter();

    expect(await screen.findByText("Failed to load sessions")).toBeInTheDocument();
  });

  it("renders status filter dropdown", async () => {
    mockListSessions.mockResolvedValue({ items: [], hasMore: false });

    renderWithRouter();

    const statusFilter = await screen.findByLabelText("Filter by status");
    expect(statusFilter).toBeInTheDocument();
  });

  it("renders machine ID filter input", async () => {
    mockListSessions.mockResolvedValue({ items: [], hasMore: false });

    renderWithRouter();

    const machineFilter = await screen.findByLabelText("Filter by machine ID");
    expect(machineFilter).toBeInTheDocument();
  });

  it("renders Sessions heading", async () => {
    mockListSessions.mockResolvedValue({ items: [], hasMore: false });

    renderWithRouter();

    expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
  });

  it("renders table headers when sessions exist", async () => {
    mockListSessions.mockResolvedValue({
      items: [
        {
          id: "s1",
          machineId: "m1",
          status: "active",
          createdAt: "2025-01-15T10:00:00Z",
          updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z",
          userId: "u1",
          createdBy: "copilot",
        },
      ],
      hasMore: false,
    });

    renderWithRouter();

    await screen.findByText("m1");
    expect(screen.getByText("Status")).toBeInTheDocument();
    expect(screen.getByText("Machine ID")).toBeInTheDocument();
    expect(screen.getByText("Repository")).toBeInTheDocument();
    expect(screen.getByText("Branch")).toBeInTheDocument();
  });

  it("shows dash for optional fields", async () => {
    mockListSessions.mockResolvedValue({
      items: [
        {
          id: "s1",
          machineId: "m1",
          status: "active",
          createdAt: "2025-01-15T10:00:00Z",
          updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z",
          userId: "u1",
          createdBy: "copilot",
        },
      ],
      hasMore: false,
    });

    renderWithRouter();

    await screen.findByText("m1");
    const dashes = screen.getAllByText("-");
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  describe("filtering", () => {
    it("changing status filter triggers re-fetch with correct params", async () => {
      const user = userEvent.setup();
      mockListSessions.mockResolvedValue({ items: [], hasMore: false });

      renderWithRouter();

      await screen.findByText("No sessions found.");
      expect(mockListSessions).toHaveBeenCalledWith({
        statusGroup: "live",
        tool: undefined,
        machineId: undefined,
        continuationToken: undefined,
      });

      mockListSessions.mockClear();
      await user.selectOptions(screen.getByLabelText("Filter by status"), "stale");

      await waitFor(() => {
        expect(mockListSessions).toHaveBeenCalledWith(
          expect.objectContaining({ statusGroup: "stale" })
        );
      });
    });

    it("changing machine filter triggers re-fetch with correct params", async () => {
      const user = userEvent.setup();
      // Return a resolved value for every call
      mockListSessions.mockResolvedValue({ items: [], hasMore: false });

      renderWithRouter();
      await screen.findByText("No sessions found.");

      const initialCallCount = mockListSessions.mock.calls.length;
      await user.type(screen.getByLabelText("Filter by machine ID"), "x");

      // Wait for the re-fetch triggered by the filter change
      await waitFor(() => {
        expect(mockListSessions.mock.calls.length).toBeGreaterThan(initialCallCount);
      });
      const lastCall = mockListSessions.mock.calls[mockListSessions.mock.calls.length - 1];
      expect(lastCall[0]).toMatchObject({ machineId: "x" });
    });
  });

  describe("pagination", () => {
    it("Load More appends sessions, does not replace", async () => {
      const user = userEvent.setup();
      const page1: Session = {
        id: "s1",
        machineId: "m1",
        status: "active",
        createdAt: "2025-01-15T10:00:00Z",
        updatedAt: "2025-01-15T10:00:00Z",
        lastHeartbeat: "2025-01-15T10:00:00Z",
        userId: "u1",
        createdBy: "copilot",
      };
      const page2: Session = {
        id: "s2",
        machineId: "m2",
        status: "closed",
        createdAt: "2025-01-15T11:00:00Z",
        updatedAt: "2025-01-15T11:00:00Z",
        lastHeartbeat: "2025-01-15T11:00:00Z",
        userId: "u1",
        createdBy: "copilot",
      };

      mockListSessions
        .mockResolvedValueOnce({ items: [page1], hasMore: true, continuationToken: "tok1" })
        .mockResolvedValueOnce({ items: [page2], hasMore: false });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.queryByText("m2")).not.toBeInTheDocument();

      await user.click(screen.getByText("Load More"));

      expect(await screen.findByText("m2")).toBeInTheDocument();
      // Page 1 data is still present
      expect(screen.getByText("m1")).toBeInTheDocument();
    });

    it("Load More passes continuation token", async () => {
      const user = userEvent.setup();
      mockListSessions.mockResolvedValueOnce({
        items: [{
          id: "s1", machineId: "m1", status: "active",
          createdAt: "2025-01-15T10:00:00Z", updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z", userId: "u1", createdBy: "copilot",
        }],
        hasMore: true,
        continuationToken: "tok-abc",
      }).mockResolvedValueOnce({ items: [], hasMore: false });

      renderWithRouter();
      await screen.findByText("m1");

      await user.click(screen.getByText("Load More"));

      await waitFor(() => {
        expect(mockListSessions).toHaveBeenCalledWith(
          expect.objectContaining({ continuationToken: "tok-abc" })
        );
      });
    });
  });

  describe("tool badge", () => {
    it("renders tool badge with explicit tool value", async () => {
      mockListSessions.mockResolvedValue({
        items: [
          {
            id: "s1", machineId: "m1", status: "active",
            tool: "claude",
            createdAt: "2025-01-15T10:00:00Z", updatedAt: "2025-01-15T10:00:00Z",
            lastHeartbeat: "2025-01-15T10:00:00Z", userId: "u1", createdBy: "copilot",
          },
        ],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.getByText("claude")).toBeInTheDocument();
    });

    it("defaults tool badge to copilot when tool is undefined", async () => {
      mockListSessions.mockResolvedValue({
        items: [
          {
            id: "s1", machineId: "m1", status: "active",
            createdAt: "2025-01-15T10:00:00Z", updatedAt: "2025-01-15T10:00:00Z",
            lastHeartbeat: "2025-01-15T10:00:00Z", userId: "u1", createdBy: "copilot",
          },
        ],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      // ToolBadge defaults to "copilot" when tool is undefined
      expect(screen.getByText("copilot")).toBeInTheDocument();
    });

    it("renders both tool badges for mixed sessions", async () => {
      mockListSessions.mockResolvedValue({
        items: [
          {
            id: "s1", machineId: "m1", status: "active", tool: "copilot",
            createdAt: "2025-01-15T10:00:00Z", updatedAt: "2025-01-15T10:00:00Z",
            lastHeartbeat: "2025-01-15T10:00:00Z", userId: "u1", createdBy: "copilot",
          },
          {
            id: "s2", machineId: "m2", status: "closed", tool: "claude",
            createdAt: "2025-01-15T11:00:00Z", updatedAt: "2025-01-15T11:00:00Z",
            lastHeartbeat: "2025-01-15T11:00:00Z", userId: "u1", createdBy: "copilot",
          },
        ],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.getByText("copilot")).toBeInTheDocument();
      expect(screen.getByText("claude")).toBeInTheDocument();
    });
  });

  describe("tool filter", () => {
    it("renders tool filter dropdown", async () => {
      mockListSessions.mockResolvedValue({ items: [], hasMore: false });

      renderWithRouter();

      const toolFilter = await screen.findByLabelText("Filter by tool");
      expect(toolFilter).toBeInTheDocument();
    });

    it("changing tool filter triggers re-fetch with correct params", async () => {
      const user = userEvent.setup();
      mockListSessions.mockResolvedValue({ items: [], hasMore: false });

      renderWithRouter();

      await screen.findByText("No sessions found.");
      mockListSessions.mockClear();
      await user.selectOptions(screen.getByLabelText("Filter by tool"), "claude");

      await waitFor(() => {
        expect(mockListSessions).toHaveBeenCalledWith(
          expect.objectContaining({ tool: "claude" })
        );
      });
    });
  });

  describe("row navigation", () => {
    it("clicking a row navigates to session detail", async () => {
      const user = userEvent.setup();
      mockListSessions.mockResolvedValue({
        items: [{
          id: "sess-1", machineId: "machine-a", status: "active",
          repository: "org/repo", branch: "main",
          createdAt: "2025-01-15T10:00:00Z", updatedAt: "2025-01-15T10:00:00Z",
          lastHeartbeat: "2025-01-15T10:00:00Z", userId: "u1", createdBy: "copilot",
        }],
        hasMore: false,
      });

      render(
        <MemoryRouter>
          <Routes>
            <Route path="/" element={<SessionList />} />
            <Route path="/sessions/:machineId/:id" element={<div>Session Detail Page</div>} />
          </Routes>
        </MemoryRouter>
      );

      await screen.findByText("machine-a");
      await user.click(screen.getByText("machine-a").closest("tr")!);

      expect(await screen.findByText("Session Detail Page")).toBeInTheDocument();
    });
  });
});
