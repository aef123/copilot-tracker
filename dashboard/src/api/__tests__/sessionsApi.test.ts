import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiGet } from "../apiClient";
import { listSessions, getSession } from "../sessionsApi";

vi.mock("../apiClient", () => ({
  apiGet: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
}));

const mockApiGet = vi.mocked(apiGet);

describe("sessionsApi", () => {
  beforeEach(() => {
    mockApiGet.mockClear();
  });

  describe("listSessions", () => {
    it("builds correct URL with no params", async () => {
      await listSessions();
      expect(mockApiGet).toHaveBeenCalledWith("/api/sessions");
    });

    it("builds correct URL with all params", async () => {
      await listSessions({
        machineId: "machine-1",
        status: "active",
        since: "2024-01-01",
        continuationToken: "token123",
        pageSize: 25,
      });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/sessions?");
      expect(url).toContain("machineId=machine-1");
      expect(url).toContain("status=active");
      expect(url).toContain("since=2024-01-01");
      expect(url).toContain("continuationToken=token123");
      expect(url).toContain("pageSize=25");
    });

    it("omits undefined params from query string", async () => {
      await listSessions({ status: "active" });
      expect(mockApiGet).toHaveBeenCalledWith("/api/sessions?status=active");
    });
  });

  describe("getSession", () => {
    it("URL-encodes machineId and id", async () => {
      await getSession("machine/1", "session/2");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/sessions/machine%2F1/session%2F2"
      );
    });

    it("builds correct URL for simple ids", async () => {
      await getSession("machine-1", "abc-123");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/sessions/machine-1/abc-123"
      );
    });
  });
});
