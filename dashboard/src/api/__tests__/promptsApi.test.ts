import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiGet } from "../apiClient";
import { listPrompts, getPrompt, getPromptLogs } from "../promptsApi";

vi.mock("../apiClient", () => ({
  apiGet: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
}));

const mockApiGet = vi.mocked(apiGet);

describe("promptsApi", () => {
  beforeEach(() => {
    mockApiGet.mockClear();
  });

  describe("listPrompts", () => {
    it("builds correct URL with no params", async () => {
      await listPrompts();
      expect(mockApiGet).toHaveBeenCalledWith("/api/prompts");
    });

    it("builds correct URL with all params", async () => {
      await listPrompts({
        sessionId: "sess-1",
        status: "started",
        continuationToken: "token456",
        pageSize: 50,
      });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/prompts?");
      expect(url).toContain("sessionId=sess-1");
      expect(url).toContain("status=started");
      expect(url).toContain("continuationToken=token456");
      expect(url).toContain("pageSize=50");
    });

    it("builds correct URL with only sessionId", async () => {
      await listPrompts({ sessionId: "sess-2" });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/prompts?");
      expect(url).toContain("sessionId=sess-2");
      expect(url).not.toContain("status=");
      expect(url).not.toContain("continuationToken=");
      expect(url).not.toContain("pageSize=");
    });
  });

  describe("getPrompt", () => {
    it("URL-encodes sessionId and id", async () => {
      await getPrompt("sess/1", "prompt/2");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/prompts/sess%2F1/prompt%2F2"
      );
    });

    it("builds correct URL for simple ids", async () => {
      await getPrompt("sess-1", "prompt-123");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/prompts/sess-1/prompt-123"
      );
    });
  });

  describe("getPromptLogs", () => {
    it("builds correct URL with no pagination params", async () => {
      await getPromptLogs("sess-1", "prompt-123");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/prompts/sess-1/prompt-123/logs"
      );
    });

    it("builds correct URL with pagination params", async () => {
      await getPromptLogs("sess-1", "prompt-123", {
        continuationToken: "logtoken",
        pageSize: 100,
      });

      const url = mockApiGet.mock.calls[0][0];
      expect(url).toContain("/api/prompts/sess-1/prompt-123/logs?");
      expect(url).toContain("continuationToken=logtoken");
      expect(url).toContain("pageSize=100");
    });

    it("URL-encodes sessionId and promptId", async () => {
      await getPromptLogs("sess/1", "prompt/2");
      expect(mockApiGet).toHaveBeenCalledWith(
        "/api/prompts/sess%2F1/prompt%2F2/logs"
      );
    });
  });
});
