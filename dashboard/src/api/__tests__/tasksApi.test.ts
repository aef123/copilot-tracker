import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiGet } from "../apiClient";
import { listTasks, getTask, getTaskLogs } from "../tasksApi";

vi.mock("../apiClient", () => ({
  apiGet: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
}));

const mockApiGet = vi.mocked(apiGet);

describe("tasksApi", () => {
  beforeEach(() => {
    mockApiGet.mockClear();
  });

  describe("listTasks", () => {
    it("builds correct URL with no params", async () => {
      await listTasks();
      expect(mockApiGet).toHaveBeenCalledWith("/api/tasks");
    });

    it("builds correct URL with all params", async () => {
      await listTasks({
        queueName: "default",
        status: "started",
        continuationToken: "token456",
        pageSize: 50,
      });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/tasks?");
      expect(url).toContain("queueName=default");
      expect(url).toContain("status=started");
      expect(url).toContain("continuationToken=token456");
      expect(url).toContain("pageSize=50");
    });
  });

  describe("getTask", () => {
    it("URL-encodes queueName and id", async () => {
      await getTask("queue/1", "task/2");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/tasks/queue%2F1/task%2F2"
      );
    });

    it("builds correct URL for simple ids", async () => {
      await getTask("default", "task-123");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/tasks/default/task-123"
      );
    });
  });

  describe("getTaskLogs", () => {
    it("builds correct URL with no pagination params", async () => {
      await getTaskLogs("default", "task-123");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/tasks/default/task-123/logs"
      );
    });

    it("builds correct URL with pagination params", async () => {
      await getTaskLogs("default", "task-123", {
        continuationToken: "logtoken",
        pageSize: 100,
      });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/tasks/default/task-123/logs?");
      expect(url).toContain("continuationToken=logtoken");
      expect(url).toContain("pageSize=100");
    });

    it("URL-encodes queueName and taskId", async () => {
      await getTaskLogs("queue/1", "task/2");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/tasks/queue%2F1/task%2F2/logs"
      );
    });
  });
});
