import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter } from "react-router-dom";
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
    expect(screen.getByText("org/repo")).toBeInTheDocument();
    expect(screen.getByText("main")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
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
});
