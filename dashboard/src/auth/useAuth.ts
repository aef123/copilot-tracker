import { useMsal, useIsAuthenticated } from "@azure/msal-react";
import { loginRequest } from "./msalConfig";

export function useAuth() {
  const { instance, accounts } = useMsal();
  const isAuthenticated = useIsAuthenticated();

  const login = async () => {
    try {
      await instance.loginRedirect(loginRequest);
    } catch (error) {
      console.error("Login failed:", error);
    }
  };

  const logout = () => {
    instance.logoutRedirect();
  };

  const getAccessToken = async (): Promise<string | null> => {
    if (accounts.length === 0) return null;

    try {
      const response = await instance.acquireTokenSilent({
        ...loginRequest,
        account: accounts[0],
      });
      return response.accessToken;
    } catch (error) {
      // If silent fails, fall back to redirect
      try {
        await instance.acquireTokenRedirect(loginRequest);
        return null; // Page will redirect, so this won't resolve
      } catch (redirectError) {
        console.error("Token acquisition failed:", redirectError);
        return null;
      }
    }
  };

  const user = accounts[0]
    ? {
        name: accounts[0].name || "Unknown",
        email: accounts[0].username,
        id: accounts[0].localAccountId,
      }
    : null;

  return { isAuthenticated, login, logout, getAccessToken, user };
}
