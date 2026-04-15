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
});
