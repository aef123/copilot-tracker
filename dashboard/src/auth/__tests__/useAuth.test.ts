import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";

// Mock MSAL hooks
const mockInstance = {
  loginPopup: vi.fn(),
  logoutPopup: vi.fn(),
  acquireTokenSilent: vi.fn(),
  acquireTokenPopup: vi.fn(),
};
const mockAccounts: any[] = [];

vi.mock("@azure/msal-react", () => ({
  useMsal: () => ({ instance: mockInstance, accounts: mockAccounts }),
  useIsAuthenticated: vi.fn(() => mockAccounts.length > 0),
}));

vi.mock("../msalConfig", () => ({
  loginRequest: { scopes: ["api://test/scope"] },
}));

import { useAuth } from "../useAuth";
import { useIsAuthenticated } from "@azure/msal-react";

describe("useAuth", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccounts.length = 0;
  });

  describe("unauthenticated state", () => {
    it("returns isAuthenticated false when no accounts", () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const { result } = renderHook(() => useAuth());

      expect(result.current.isAuthenticated).toBe(false);
      expect(result.current.user).toBeNull();
    });
  });

  describe("authenticated state", () => {
    it("returns user info from first account", () => {
      mockAccounts.push({
        name: "Test User",
        username: "test@example.com",
        localAccountId: "user-123",
      });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result } = renderHook(() => useAuth());

      expect(result.current.isAuthenticated).toBe(true);
      expect(result.current.user).toEqual({
        name: "Test User",
        email: "test@example.com",
        id: "user-123",
      });
    });

    it("uses 'Unknown' when account name is missing", () => {
      mockAccounts.push({
        name: undefined,
        username: "test@example.com",
        localAccountId: "user-123",
      });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result } = renderHook(() => useAuth());

      expect(result.current.user?.name).toBe("Unknown");
    });
  });

  describe("login", () => {
    it("calls loginPopup with loginRequest", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      mockInstance.loginPopup.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(mockInstance.loginPopup).toHaveBeenCalledWith({ scopes: ["api://test/scope"] });
    });

    it("handles login failure gracefully", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.loginPopup.mockRejectedValue(new Error("user cancelled"));

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(consoleSpy).toHaveBeenCalledWith("Login failed:", expect.any(Error));
      consoleSpy.mockRestore();
    });
  });

  describe("logout", () => {
    it("calls logoutPopup", () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result } = renderHook(() => useAuth());
      result.current.logout();

      expect(mockInstance.logoutPopup).toHaveBeenCalled();
    });
  });

  describe("getAccessToken", () => {
    it("returns null when no accounts", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
    });

    it("returns token from silent acquisition", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockResolvedValue({ accessToken: "silent-token" });

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBe("silent-token");
    });

    it("falls back to popup when silent fails", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockRejectedValue(new Error("interaction required"));
      mockInstance.acquireTokenPopup.mockResolvedValue({ accessToken: "popup-token" });

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBe("popup-token");
    });

    it("returns null when both silent and popup fail", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.acquireTokenSilent.mockRejectedValue(new Error("fail1"));
      mockInstance.acquireTokenPopup.mockRejectedValue(new Error("fail2"));

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
      expect(consoleSpy).toHaveBeenCalledWith("Token acquisition failed:", expect.any(Error));
      consoleSpy.mockRestore();
    });
  });
});
