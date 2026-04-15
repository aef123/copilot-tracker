import { describe, it, expect, vi, beforeEach } from "vitest";
import { getHealth } from "../healthApi";

describe("healthApi", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn());
  });

  it("returns health data on success", async () => {
    const healthData = {
      activeSessions: 3,
      completedSessions: 10,
      staleSessions: 1,
      totalTasks: 42,
      activeTasks: 5,
      timestamp: "2025-01-15T10:00:00Z",
    };

    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(healthData),
    });

    const result = await getHealth();
    expect(result).toEqual(healthData);
    expect(globalThis.fetch).toHaveBeenCalledWith("/api/health");
  });

  it("throws on non-ok response with status code", async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 503,
    });

    await expect(getHealth()).rejects.toThrow("Health check failed: 503");
  });

  it("throws on network failure", async () => {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(
      new TypeError("Failed to fetch")
    );

    await expect(getHealth()).rejects.toThrow("Failed to fetch");
  });
});
