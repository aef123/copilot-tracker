import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { HealthDashboard } from "../HealthDashboard";
import type { HealthSummary } from "../../api";

vi.mock("../../api", () => ({
  getHealth: vi.fn(),
}));

import { getHealth } from "../../api";
const mockGetHealth = vi.mocked(getHealth);

describe("HealthDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockGetHealth.mockReturnValue(new Promise(() => {})); // never resolves
    render(<HealthDashboard />);
    expect(screen.getByText("Loading dashboard...")).toBeInTheDocument();
  });

  it("renders health counts when data loads", async () => {
    const health: HealthSummary = {
      activeSessions: 3,
      completedSessions: 12,
      staleSessions: 1,
      totalTasks: 42,
      activeTasks: 5,
      timestamp: "2025-01-15T10:00:00Z",
    };
    mockGetHealth.mockResolvedValue(health);

    render(<HealthDashboard />);

    expect(await screen.findByText("3")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("Active Sessions")).toBeInTheDocument();
  });

  it("renders error state on failure", async () => {
    mockGetHealth.mockRejectedValue(new Error("Network error"));

    render(<HealthDashboard />);

    expect(await screen.findByText("Network error")).toBeInTheDocument();
  });

  it("handles non-Error thrown from API", async () => {
    mockGetHealth.mockRejectedValue("string error");

    render(<HealthDashboard />);

    expect(await screen.findByText("Failed to load health data")).toBeInTheDocument();
  });

  it("renders last updated timestamp", async () => {
    const health: HealthSummary = {
      activeSessions: 1,
      completedSessions: 2,
      staleSessions: 0,
      totalTasks: 5,
      activeTasks: 1,
      timestamp: "2025-01-15T10:00:00Z",
    };
    mockGetHealth.mockResolvedValue(health);

    render(<HealthDashboard />);

    expect(await screen.findByText(/Last updated:/)).toBeInTheDocument();
  });

  it("renders all five health cards", async () => {
    const health: HealthSummary = {
      activeSessions: 1,
      completedSessions: 2,
      staleSessions: 3,
      totalTasks: 4,
      activeTasks: 5,
      timestamp: "2025-01-15T10:00:00Z",
    };
    mockGetHealth.mockResolvedValue(health);

    render(<HealthDashboard />);

    expect(await screen.findByText("Active Sessions")).toBeInTheDocument();
    expect(screen.getByText("Completed Sessions")).toBeInTheDocument();
    expect(screen.getByText("Stale Sessions")).toBeInTheDocument();
    expect(screen.getByText("Total Tasks")).toBeInTheDocument();
    expect(screen.getByText("Active Tasks")).toBeInTheDocument();
  });

  it("renders Dashboard heading", async () => {
    mockGetHealth.mockResolvedValue({
      activeSessions: 0,
      completedSessions: 0,
      staleSessions: 0,
      totalTasks: 0,
      activeTasks: 0,
      timestamp: "2025-01-15T10:00:00Z",
    });

    render(<HealthDashboard />);

    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
  });
});
