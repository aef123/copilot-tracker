import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { SessionGrid } from "../SessionGrid";
import type { Session } from "../../api";

vi.mock("../../api", () => ({
  listSessions: vi.fn(),
}));

import { listSessions } from "../../api";
const mockListSessions = vi.mocked(listSessions);

function renderWithRouter() {
  return render(
    <MemoryRouter>
      <SessionGrid />
    </MemoryRouter>
  );
}

function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    id: "s1",
    machineId: "m1",
    status: "active",
    createdAt: "2025-01-15T10:00:00Z",
    updatedAt: "2025-01-15T10:00:00Z",
    lastHeartbeat: "2025-01-15T10:00:00Z",
    userId: "u1",
    createdBy: "copilot",
    ...overrides,
  };
}

describe("SessionGrid", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders session cards", async () => {
    mockListSessions.mockResolvedValue({
      items: [makeSession({ machineId: "machine-a", repository: "org/repo" })],
      hasMore: false,
    });

    renderWithRouter();

    expect(await screen.findByText("machine-a")).toBeInTheDocument();
    expect(screen.getByText("repo")).toBeInTheDocument();
  });

  it("shows loading state initially", () => {
    mockListSessions.mockReturnValue(new Promise(() => {}));
    renderWithRouter();
    expect(screen.getByText("Loading sessions...")).toBeInTheDocument();
  });

  it("shows empty state when no sessions", async () => {
    mockListSessions.mockResolvedValue({ items: [], hasMore: false });

    renderWithRouter();

    expect(await screen.findByText("No sessions found.")).toBeInTheDocument();
  });

  it("renders error state on failure", async () => {
    mockListSessions.mockRejectedValue(new Error("Server down"));

    renderWithRouter();

    expect(await screen.findByText("Server down")).toBeInTheDocument();
  });

  describe("tool badge", () => {
    it("renders tool badge with explicit tool value", async () => {
      mockListSessions.mockResolvedValue({
        items: [makeSession({ id: "s1", machineId: "m1", tool: "claude" })],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.getByText("claude")).toBeInTheDocument();
    });

    it("defaults tool badge to copilot when tool is undefined", async () => {
      mockListSessions.mockResolvedValue({
        items: [makeSession({ id: "s1", machineId: "m1" })],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.getByText("copilot")).toBeInTheDocument();
    });

    it("renders both tool badges for mixed sessions", async () => {
      mockListSessions.mockResolvedValue({
        items: [
          makeSession({ id: "s1", machineId: "m1", tool: "copilot" }),
          makeSession({ id: "s2", machineId: "m2", tool: "claude" }),
        ],
        hasMore: false,
      });

      renderWithRouter();

      await screen.findByText("m1");
      expect(screen.getByText("copilot")).toBeInTheDocument();
      expect(screen.getByText("claude")).toBeInTheDocument();
    });
  });
});
