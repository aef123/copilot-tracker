import type { Configuration } from "@azure/msal-browser";
import { LogLevel } from "@azure/msal-browser";

const clientId = import.meta.env.VITE_AZURE_CLIENT_ID || "4c8148f5-c913-40c5-863f-1c019821eac4";
const tenantId = import.meta.env.VITE_AZURE_TENANT_ID || "5df6d88f-0d78-491b-9617-8b43a209ba73";

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: "sessionStorage",

  },
  system: {
    loggerOptions: {
      logLevel: LogLevel.Warning,
      loggerCallback: (level, message) => {
        if (level === LogLevel.Error) {
          console.error(message);
        }
      },
    },
  },
};

export const loginRequest = {
  scopes: [`api://${clientId}/CopilotTracker.ReadWrite`],
};

export const apiScope = `api://${clientId}/CopilotTracker.ReadWrite`;
