import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";

// Control auth state for the real App component
let isAuthenticated = true;
const mockLogin = vi.fn();

vi.mock("@azure/msal-react", () => ({
  AuthenticatedTemplate: ({ children }: { children: React.ReactNode }) =>
    isAuthenticated ? <>{children}</> : null,
  UnauthenticatedTemplate: ({ children }: { children: React.ReactNode }) =>
    isAuthenticated ? null : <>{children}</>,
  useMsal: () => ({
    instance: { loginPopup: mockLogin, logoutPopup: vi.fn() },
    accounts: isAuthenticated
      ? [{ name: "Test User", username: "test@example.com", localAccountId: "u1" }]
      : [],
  }),
  useIsAuthenticated: () => isAuthenticated,
}));

vi.mock("../auth/msalConfig", () => ({
  loginRequest: { scopes: ["api://test/scope"] },
}));

// Mock API calls
vi.mock("../api", () => ({
  getHealth: vi.fn().mockResolvedValue({
    activeSessions: 1,
    completedSessions: 2,
    staleSessions: 0,
    totalTasks: 5,
    activeTasks: 1,
    timestamp: "2025-01-15T10:00:00Z",
  }),
  listSessions: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
  getSession: vi.fn().mockResolvedValue({
    id: "sess-1",
    machineId: "machine-1",
    repository: "org/repo",
    branch: "main",
    status: "active",
    createdAt: "2025-01-15T10:00:00Z",
    updatedAt: "2025-01-15T10:05:00Z",
    lastHeartbeat: "2025-01-15T10:04:00Z",
    userId: "user-1",
    createdBy: "copilot",
  }),
  listTasks: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
  getTask: vi.fn().mockResolvedValue({
    id: "task-1",
    sessionId: "sess-1",
    queueName: "default",
    title: "Build project",
    status: "done",
    result: "Build succeeded",
    source: "prompt",
    createdAt: "2025-01-15T10:00:00Z",
    updatedAt: "2025-01-15T10:01:00Z",
    userId: "user-1",
    createdBy: "copilot",
  }),
  getTaskLogs: vi.fn().mockResolvedValue({ items: [], hasMore: false }),
}));

// Replace BrowserRouter with MemoryRouter so we can control the initial URL
// while still rendering the REAL App component and all its actual routes.
let testEntries: string[] = ["/"];
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return {
    ...actual,
    BrowserRouter: ({ children }: { children: React.ReactNode }) => {
      const { MemoryRouter } = actual;
      return <MemoryRouter initialEntries={testEntries}>{children}</MemoryRouter>;
    },
  };
});

import App from "../App";

describe("App (real component)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isAuthenticated = true;
    testEntries = ["/"];
  });

  describe("authenticated routes", () => {
    it("renders HealthDashboard at /", async () => {
      testEntries = ["/"];
      render(<App />);
      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    });

    it("renders SessionList at /sessions", async () => {
      testEntries = ["/sessions"];
      render(<App />);
      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("renders SessionDetail at /sessions/:machineId/:id", async () => {
      testEntries = ["/sessions/machine-1/sess-1"];
      render(<App />);
      expect(await screen.findByText("Session Details")).toBeInTheDocument();
    });

    it("renders TaskDetail at /tasks/:queueName/:id", async () => {
      testEntries = ["/tasks/default/task-1"];
      render(<App />);
      expect(await screen.findByText("Task Details")).toBeInTheDocument();
    });

    it("renders nothing for unknown routes (no wildcard route defined)", async () => {
      testEntries = ["/unknown/path"];
      render(<App />);
      // Real App has no catch-all route, so nothing matches and no content renders
      // The authenticated template is active but no route matches
      expect(screen.queryByText("Session Details")).not.toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Dashboard" })).not.toBeInTheDocument();
    });

    it("renders nothing for partially matching routes", async () => {
      testEntries = ["/sessions/only-one-param"];
      render(<App />);
      // No route matches /sessions/:singleParam
      expect(screen.queryByText("Session Details")).not.toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Sessions" })).not.toBeInTheDocument();
    });

    it("Layout wraps all authenticated routes with nav", async () => {
      testEntries = ["/"];
      render(<App />);
      expect(await screen.findByText("Copilot Session Tracker")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Sessions" })).toBeInTheDocument();
    });
  });

  describe("unauthenticated state", () => {
    it("shows login page when not authenticated", () => {
      isAuthenticated = false;
      render(<App />);

      expect(screen.getByText("Sign in to view your sessions.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Sign In" })).toBeInTheDocument();
    });

    it("does not render any routes when not authenticated", () => {
      isAuthenticated = false;
      testEntries = ["/sessions"];
      render(<App />);

      expect(screen.getByText("Sign in to view your sessions.")).toBeInTheDocument();
      expect(screen.queryByRole("heading", { name: "Sessions" })).not.toBeInTheDocument();
    });

    it("shows login for deep links when not authenticated", () => {
      isAuthenticated = false;
      testEntries = ["/sessions/machine-1/sess-1"];
      render(<App />);

      expect(screen.getByText("Sign in to view your sessions.")).toBeInTheDocument();
      expect(screen.queryByText("Session Details")).not.toBeInTheDocument();
    });

    it("login button calls the login function from useAuth", async () => {
      isAuthenticated = false;
      const user = userEvent.setup();
      render(<App />);

      await user.click(screen.getByRole("button", { name: "Sign In" }));

      expect(mockLogin).toHaveBeenCalled();
    });
  });

  describe("deep linking", () => {
    it("deep link to session detail renders correct data", async () => {
      testEntries = ["/sessions/machine-1/sess-1"];
      render(<App />);

      expect(await screen.findByText("sess-1")).toBeInTheDocument();
      expect(screen.getByText("machine-1")).toBeInTheDocument();
    });

    it("deep link to task detail renders correct data", async () => {
      testEntries = ["/tasks/default/task-1"];
      render(<App />);

      expect(await screen.findByText("task-1")).toBeInTheDocument();
      expect(await screen.findByText("Build project")).toBeInTheDocument();
    });

    it("deep link with encoded path segments works", async () => {
      testEntries = ["/sessions/machine%201/sess-1"];
      render(<App />);

      expect(await screen.findByText("Session Details")).toBeInTheDocument();
    });
  });

  describe("navigation between routes", () => {
    it("navigating from Dashboard to Sessions works", async () => {
      const user = userEvent.setup();
      testEntries = ["/"];
      render(<App />);

      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
      await user.click(screen.getByRole("link", { name: "Sessions" }));
      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("navigating from Sessions back to Dashboard works", async () => {
      const user = userEvent.setup();
      testEntries = ["/sessions"];
      render(<App />);

      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
      await user.click(screen.getByRole("link", { name: "Dashboard" }));
      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    });

    it("navigating from SessionDetail back to sessions list works", async () => {
      const user = userEvent.setup();
      testEntries = ["/sessions/machine-1/sess-1"];
      render(<App />);

      const backLink = await screen.findByText(/Back to Sessions/);
      await user.click(backLink);
      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("navigating from TaskDetail back to sessions list works", async () => {
      const user = userEvent.setup();
      testEntries = ["/tasks/default/task-1"];
      render(<App />);

      const backLink = await screen.findByText(/Back to Sessions/);
      await user.click(backLink);
      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });
  });
});
