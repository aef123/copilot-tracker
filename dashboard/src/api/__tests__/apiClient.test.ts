import { describe, it, expect, vi, beforeEach } from "vitest";

// Mock the auth module before importing apiClient
vi.mock("../../auth/AuthProvider", () => ({
  msalInstance: {
    getAllAccounts: vi.fn(),
    acquireTokenSilent: vi.fn(),
  },
}));

vi.mock("../../auth/msalConfig", () => ({
  loginRequest: { scopes: ["api://test/scope"] },
}));

import { msalInstance } from "../../auth/AuthProvider";
import { apiFetch, apiGet, apiPost, apiPut } from "../apiClient";

const mockMsal = vi.mocked(msalInstance);

describe("apiClient", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal("fetch", vi.fn());
  });

  function setupAuth(token = "test-token") {
    mockMsal.getAllAccounts.mockReturnValue([{ username: "user@test.com" }] as any);
    mockMsal.acquireTokenSilent.mockResolvedValue({ accessToken: token } as any);
  }

  function mockFetchResponse(body: unknown, status = 200) {
    (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(body),
      text: () => Promise.resolve(JSON.stringify(body)),
    });
  }

  describe("getAuthHeaders", () => {
    it("throws when no accounts exist", async () => {
      mockMsal.getAllAccounts.mockReturnValue([]);
      await expect(apiGet("/api/test")).rejects.toThrow("No authenticated user");
    });

    it("throws when token acquisition fails", async () => {
      mockMsal.getAllAccounts.mockReturnValue([{ username: "u" }] as any);
      mockMsal.acquireTokenSilent.mockRejectedValue(new Error("token error"));
      await expect(apiGet("/api/test")).rejects.toThrow("Failed to acquire token");
    });

    it("attaches Bearer token to requests", async () => {
      setupAuth("my-access-token");
      mockFetchResponse({ ok: true });

      await apiGet("/api/test");

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/test",
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: "Bearer my-access-token",
            "Content-Type": "application/json",
          }),
        })
      );
    });
  });

  describe("apiFetch", () => {
    it("throws on non-ok response with status and body", async () => {
      setupAuth();
      (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: false,
        status: 404,
        text: () => Promise.resolve("Not Found"),
      });

      await expect(apiFetch("/api/missing")).rejects.toThrow("API error 404: Not Found");
    });

    it("throws on 401 Unauthorized", async () => {
      setupAuth();
      (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: false,
        status: 401,
        text: () => Promise.resolve("Unauthorized"),
      });

      await expect(apiFetch("/api/secure")).rejects.toThrow("API error 401: Unauthorized");
    });

    it("throws on 500 server error", async () => {
      setupAuth();
      (globalThis.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
        ok: false,
        status: 500,
        text: () => Promise.resolve("Internal Server Error"),
      });

      await expect(apiFetch("/api/broken")).rejects.toThrow("API error 500: Internal Server Error");
    });

    it("throws on network failure", async () => {
      setupAuth();
      (globalThis.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new TypeError("Failed to fetch"));

      await expect(apiFetch("/api/test")).rejects.toThrow("Failed to fetch");
    });

    it("parses JSON response body", async () => {
      setupAuth();
      mockFetchResponse({ id: 1, name: "test" });

      const result = await apiFetch<{ id: number; name: string }>("/api/items");
      expect(result).toEqual({ id: 1, name: "test" });
    });

    it("merges custom headers with auth headers", async () => {
      setupAuth();
      mockFetchResponse({});

      await apiFetch("/api/test", {
        headers: { "X-Custom": "value" },
      });

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/test",
        expect.objectContaining({
          headers: expect.objectContaining({
            Authorization: "Bearer test-token",
            "X-Custom": "value",
          }),
        })
      );
    });
  });

  describe("apiGet", () => {
    it("sends GET request", async () => {
      setupAuth();
      mockFetchResponse([]);

      await apiGet("/api/items");

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/items",
        expect.objectContaining({ method: "GET" })
      );
    });
  });

  describe("apiPost", () => {
    it("sends POST request with JSON body", async () => {
      setupAuth();
      mockFetchResponse({ id: "new" });

      await apiPost("/api/items", { name: "test" });

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/items",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ name: "test" }),
        })
      );
    });

    it("sends POST request with no body", async () => {
      setupAuth();
      mockFetchResponse({});

      await apiPost("/api/action");

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/action",
        expect.objectContaining({
          method: "POST",
          body: undefined,
        })
      );
    });
  });

  describe("apiPut", () => {
    it("sends PUT request with JSON body", async () => {
      setupAuth();
      mockFetchResponse({});

      await apiPut("/api/items/1", { name: "updated" });

      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/items/1",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify({ name: "updated" }),
        })
      );
    });
  });
});
