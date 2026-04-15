import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";

// Mock MSAL hooks
const mockInstance = {
  loginRedirect: vi.fn(),
  logoutRedirect: vi.fn(),
  acquireTokenSilent: vi.fn(),
  acquireTokenRedirect: vi.fn(),
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
    it("calls loginRedirect with loginRequest", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      mockInstance.loginRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(mockInstance.loginRedirect).toHaveBeenCalledWith({ scopes: ["api://test/scope"] });
    });

    it("handles login failure gracefully", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.loginRedirect.mockRejectedValue(new Error("user cancelled"));

      const { result } = renderHook(() => useAuth());

      await act(async () => {
        await result.current.login();
      });

      expect(consoleSpy).toHaveBeenCalledWith("Login failed:", expect.any(Error));
      consoleSpy.mockRestore();
    });
  });

  describe("logout", () => {
    it("calls logoutRedirect", () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result } = renderHook(() => useAuth());
      result.current.logout();

      expect(mockInstance.logoutRedirect).toHaveBeenCalled();
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

    it("falls back to redirect when silent fails", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockRejectedValue(new Error("interaction required"));
      mockInstance.acquireTokenRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
    });

    it("returns null when both silent and redirect fail", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.acquireTokenSilent.mockRejectedValue(new Error("fail1"));
      mockInstance.acquireTokenRedirect.mockRejectedValue(new Error("fail2"));

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
      expect(consoleSpy).toHaveBeenCalledWith("Token acquisition failed:", expect.any(Error));
      consoleSpy.mockRestore();
    });

    it("falls back to redirect on interaction_required error", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      const interactionError = new Error("interaction_required");
      interactionError.name = "InteractionRequiredAuthError";
      mockInstance.acquireTokenSilent.mockRejectedValue(interactionError);
      mockInstance.acquireTokenRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
      expect(mockInstance.acquireTokenRedirect).toHaveBeenCalledWith({ scopes: ["api://test/scope"] });
    });

    it("handles network failure during silent token acquisition", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockRejectedValue(new TypeError("Failed to fetch"));
      mockInstance.acquireTokenRedirect.mockResolvedValue(undefined);

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
    });

    it("returns null when redirect is cancelled by user", async () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.acquireTokenSilent.mockRejectedValue(new Error("silent failed"));
      const cancelError = new Error("user_cancelled");
      cancelError.name = "BrowserAuthError";
      mockInstance.acquireTokenRedirect.mockRejectedValue(cancelError);

      const { result } = renderHook(() => useAuth());
      const token = await result.current.getAccessToken();

      expect(token).toBeNull();
      consoleSpy.mockRestore();
    });

    it("passes correct account to acquireTokenSilent", async () => {
      const account = { name: "User", username: "u@t.com", localAccountId: "1" };
      mockAccounts.push(account);
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockResolvedValue({ accessToken: "tok" });

      const { result } = renderHook(() => useAuth());
      await result.current.getAccessToken();

      expect(mockInstance.acquireTokenSilent).toHaveBeenCalledWith({
        scopes: ["api://test/scope"],
        account,
      });
    });

    it("uses first account when multiple accounts exist", async () => {
      const account1 = { name: "User1", username: "u1@t.com", localAccountId: "1" };
      const account2 = { name: "User2", username: "u2@t.com", localAccountId: "2" };
      mockAccounts.push(account1, account2);
      vi.mocked(useIsAuthenticated).mockReturnValue(true);
      mockInstance.acquireTokenSilent.mockResolvedValue({ accessToken: "tok" });

      const { result } = renderHook(() => useAuth());
      await result.current.getAccessToken();

      expect(mockInstance.acquireTokenSilent).toHaveBeenCalledWith(
        expect.objectContaining({ account: account1 })
      );
    });
  });

  describe("login edge cases", () => {
    it("handles user cancelling login redirect", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      const cancelError = new Error("user_cancelled: User closed the popup");
      cancelError.name = "BrowserAuthError";
      mockInstance.loginRedirect.mockRejectedValue(cancelError);

      const { result } = renderHook(() => useAuth());
      await act(async () => {
        await result.current.login();
      });

      expect(consoleSpy).toHaveBeenCalledWith("Login failed:", cancelError);
      consoleSpy.mockRestore();
    });

    it("handles multiple rapid login attempts without throwing", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.loginRedirect
        .mockRejectedValueOnce(new Error("interaction_in_progress"))
        .mockResolvedValueOnce(undefined);

      const { result } = renderHook(() => useAuth());

      // First attempt fails with interaction_in_progress
      await act(async () => { await result.current.login(); });
      expect(consoleSpy).toHaveBeenCalledTimes(1);

      // Second attempt succeeds
      await act(async () => { await result.current.login(); });
      expect(mockInstance.loginRedirect).toHaveBeenCalledTimes(2);
      consoleSpy.mockRestore();
    });

    it("handles network failure during login", async () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
      mockInstance.loginRedirect.mockRejectedValue(new TypeError("Failed to fetch"));

      const { result } = renderHook(() => useAuth());
      await act(async () => {
        await result.current.login();
      });

      expect(consoleSpy).toHaveBeenCalledWith("Login failed:", expect.any(TypeError));
      consoleSpy.mockRestore();
    });
  });

  describe("logout edge cases", () => {
    it("can be called when already unauthenticated", () => {
      vi.mocked(useIsAuthenticated).mockReturnValue(false);

      const { result } = renderHook(() => useAuth());
      result.current.logout();

      expect(mockInstance.logoutRedirect).toHaveBeenCalled();
    });
  });

  describe("account detection", () => {
    it("updates user when account changes", () => {
      mockAccounts.push({ name: "First", username: "first@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result, rerender } = renderHook(() => useAuth());
      expect(result.current.user?.name).toBe("First");

      // Simulate account change
      mockAccounts.length = 0;
      mockAccounts.push({ name: "Second", username: "second@t.com", localAccountId: "2" });
      rerender();

      expect(result.current.user?.name).toBe("Second");
      expect(result.current.user?.email).toBe("second@t.com");
    });

    it("becomes null user when all accounts removed", () => {
      mockAccounts.push({ name: "User", username: "u@t.com", localAccountId: "1" });
      vi.mocked(useIsAuthenticated).mockReturnValue(true);

      const { result, rerender } = renderHook(() => useAuth());
      expect(result.current.user).not.toBeNull();

      mockAccounts.length = 0;
      vi.mocked(useIsAuthenticated).mockReturnValue(false);
      rerender();

      expect(result.current.user).toBeNull();
    });
  });
});
