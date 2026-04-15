import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockInitialize, mockGetAllAccounts, mockSetActiveAccount, mockAddEventCallback } =
  vi.hoisted(() => ({
    mockInitialize: vi.fn(),
    mockGetAllAccounts: vi.fn(),
    mockSetActiveAccount: vi.fn(),
    mockAddEventCallback: vi.fn(),
  }));

vi.mock("@azure/msal-browser", () => {
  function MockPCA() {
    return {
      initialize: mockInitialize,
      getAllAccounts: mockGetAllAccounts,
      setActiveAccount: mockSetActiveAccount,
      addEventCallback: mockAddEventCallback,
    };
  }
  return {
    PublicClientApplication: MockPCA,
    EventType: { LOGIN_SUCCESS: "msal:loginSuccess" },
    LogLevel: { Warning: 2, Error: 4 },
  };
});

vi.mock("@azure/msal-react", () => ({
  MsalProvider: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="msal-provider">{children}</div>
  ),
}));

vi.mock("../msalConfig", () => ({
  msalConfig: {
    auth: { clientId: "test-client-id", authority: "https://login.microsoftonline.com/test" },
    cache: { cacheLocation: "sessionStorage" },
    system: { loggerOptions: {} },
  },
}));

import { AuthProvider } from "../AuthProvider";

describe("AuthProvider", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAllAccounts.mockReturnValue([]);
    mockInitialize.mockResolvedValue(undefined);
  });

  it("shows Loading while MSAL initializes", () => {
    mockInitialize.mockReturnValue(new Promise(() => {})); // never resolves
    render(
      <AuthProvider>
        <div>App Content</div>
      </AuthProvider>
    );

    expect(screen.getByText("Loading...")).toBeInTheDocument();
    expect(screen.queryByText("App Content")).not.toBeInTheDocument();
  });

  it("renders children inside MsalProvider after initialization", async () => {
    render(
      <AuthProvider>
        <div>App Content</div>
      </AuthProvider>
    );

    expect(await screen.findByText("App Content")).toBeInTheDocument();
    expect(screen.getByTestId("msal-provider")).toBeInTheDocument();
  });

  it("sets active account when accounts exist on init", async () => {
    const account = { name: "User", username: "u@test.com", localAccountId: "1" };
    mockGetAllAccounts.mockReturnValue([account]);

    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    expect(mockSetActiveAccount).toHaveBeenCalledWith(account);
  });

  it("does not set active account when no accounts exist", async () => {
    mockGetAllAccounts.mockReturnValue([]);

    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    expect(mockSetActiveAccount).not.toHaveBeenCalled();
  });

  it("registers event callback for LOGIN_SUCCESS", async () => {
    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    expect(mockAddEventCallback).toHaveBeenCalledWith(expect.any(Function));
  });

  it("LOGIN_SUCCESS event sets active account from payload", async () => {
    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    const callback = mockAddEventCallback.mock.calls[0][0];
    const account = { name: "New User", username: "new@test.com" };

    callback({ eventType: "msal:loginSuccess", payload: { account } });

    expect(mockSetActiveAccount).toHaveBeenCalledWith(account);
  });

  it("non-LOGIN_SUCCESS events do not set active account", async () => {
    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    const callback = mockAddEventCallback.mock.calls[0][0];

    callback({ eventType: "msal:otherEvent", payload: null });

    // setActiveAccount only called if accounts existed on init, not from event
    expect(mockSetActiveAccount).not.toHaveBeenCalled();
  });

  it("stays on loading if init never resolves", async () => {
    // Simulate MSAL init that hangs or fails (never completes)
    mockInitialize.mockReturnValue(new Promise(() => {}));

    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    // Stays on loading since setIsInitialized is never called
    expect(screen.getByText("Loading...")).toBeInTheDocument();
    expect(screen.queryByText("Content")).not.toBeInTheDocument();
  });

  it("sets first account as active when multiple accounts exist", async () => {
    const account1 = { name: "User1", username: "u1@test.com", localAccountId: "1" };
    const account2 = { name: "User2", username: "u2@test.com", localAccountId: "2" };
    mockGetAllAccounts.mockReturnValue([account1, account2]);

    render(
      <AuthProvider>
        <div>Content</div>
      </AuthProvider>
    );

    await screen.findByText("Content");
    expect(mockSetActiveAccount).toHaveBeenCalledWith(account1);
  });
});
