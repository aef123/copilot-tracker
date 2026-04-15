import { MsalProvider } from "@azure/msal-react";
import { PublicClientApplication, EventType } from "@azure/msal-browser";
import { msalConfig } from "./msalConfig";
import { type ReactNode, useEffect, useState } from "react";

const msalInstance = new PublicClientApplication(msalConfig);

interface AuthProviderProps {
  children: ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    msalInstance.initialize().then(async () => {
      // Handle redirect response before checking accounts
      await msalInstance.handleRedirectPromise();

      const accounts = msalInstance.getAllAccounts();
      if (accounts.length > 0) {
        msalInstance.setActiveAccount(accounts[0]);
      }

      msalInstance.addEventCallback((event) => {
        if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
          const payload = event.payload as { account: any };
          msalInstance.setActiveAccount(payload.account);
        }
      });

      setIsInitialized(true);
    });
  }, []);

  if (!isInitialized) {
    return <div>Loading...</div>;
  }

  return <MsalProvider instance={msalInstance}>{children}</MsalProvider>;
}

export { msalInstance };
