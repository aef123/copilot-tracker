import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter, Routes, Route } from "react-router-dom";

// Mock MSAL templates to control auth state
let isAuthenticated = true;

vi.mock("@azure/msal-react", () => ({
  AuthenticatedTemplate: ({ children }: { children: React.ReactNode }) =>
    isAuthenticated ? <>{children}</> : null,
  UnauthenticatedTemplate: ({ children }: { children: React.ReactNode }) =>
    isAuthenticated ? null : <>{children}</>,
  useMsal: () => ({
    instance: { loginPopup: vi.fn(), logoutPopup: vi.fn() },
    accounts: isAuthenticated
      ? [{ name: "Test User", username: "test@example.com", localAccountId: "u1" }]
      : [],
  }),
  useIsAuthenticated: () => isAuthenticated,
}));

vi.mock("../auth/msalConfig", () => ({
  loginRequest: { scopes: ["api://test/scope"] },
}));

// Mock all API calls
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

// App uses BrowserRouter internally, so we test routing by importing the
// inner pieces and wrapping with MemoryRouter ourselves. We replicate App's
// route structure so we can control the initial URL.
import { useAuth } from "../auth/useAuth";
import { Layout } from "../components/Layout";
import { HealthDashboard } from "../components/HealthDashboard";
import { SessionList } from "../components/SessionList";
import { SessionDetail } from "../components/SessionDetail";
import { TaskDetail } from "../components/TaskDetail";

function AppRoutes({ initialEntries = ["/"] }: { initialEntries?: string[] }) {
  return (
    <MemoryRouter initialEntries={initialEntries}>
      {isAuthenticated ? (
        <Routes>
          <Route element={<Layout />}>
            <Route path="/" element={<HealthDashboard />} />
            <Route path="/sessions" element={<SessionList />} />
            <Route path="/sessions/:machineId/:id" element={<SessionDetail />} />
            <Route path="/tasks/:queueName/:id" element={<TaskDetail />} />
            <Route path="*" element={<div>Page Not Found</div>} />
          </Route>
        </Routes>
      ) : (
        <div className="login-page">
          <h1>Copilot Session Tracker</h1>
          <p>Sign in to view your sessions.</p>
          <button className="btn-primary" onClick={() => {}}>
            Sign In
          </button>
        </div>
      )}
    </MemoryRouter>
  );
}

describe("App Routing", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    isAuthenticated = true;
  });

  describe("authenticated routes", () => {
    it("renders HealthDashboard at /", async () => {
      render(<AppRoutes initialEntries={["/"]} />);
      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    });

    it("renders SessionList at /sessions", async () => {
      render(<AppRoutes initialEntries={["/sessions"]} />);
      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("renders SessionDetail at /sessions/:machineId/:id", async () => {
      render(<AppRoutes initialEntries={["/sessions/machine-1/sess-1"]} />);
      expect(await screen.findByText("Session Details")).toBeInTheDocument();
    });

    it("renders TaskDetail at /tasks/:queueName/:id", async () => {
      render(<AppRoutes initialEntries={["/tasks/default/task-1"]} />);
      expect(await screen.findByText("Task Details")).toBeInTheDocument();
    });

    it("shows 404 for unknown routes", async () => {
      render(<AppRoutes initialEntries={["/unknown/path"]} />);
      expect(await screen.findByText("Page Not Found")).toBeInTheDocument();
    });

    it("shows 404 for partially matching routes", async () => {
      render(<AppRoutes initialEntries={["/sessions/only-one-param"]} />);
      expect(await screen.findByText("Page Not Found")).toBeInTheDocument();
    });

    it("Layout wraps all authenticated routes with nav", async () => {
      render(<AppRoutes initialEntries={["/"]} />);
      expect(await screen.findByText("Copilot Session Tracker")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Sessions" })).toBeInTheDocument();
    });
  });

  describe("unauthenticated state", () => {
    it("shows login page when not authenticated", () => {
      isAuthenticated = false;
      render(<AppRoutes initialEntries={["/"]} />);

      expect(screen.getByText("Sign in to view your sessions.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Sign In" })).toBeInTheDocument();
    });

    it("shows login page for deep links when not authenticated", () => {
      isAuthenticated = false;
      render(<AppRoutes initialEntries={["/sessions/machine-1/sess-1"]} />);

      expect(screen.getByText("Sign in to view your sessions.")).toBeInTheDocument();
      expect(screen.queryByText("Session Details")).not.toBeInTheDocument();
    });

    it("shows login page for any route when not authenticated", () => {
      isAuthenticated = false;
      render(<AppRoutes initialEntries={["/tasks/default/task-1"]} />);

      expect(screen.getByRole("button", { name: "Sign In" })).toBeInTheDocument();
      expect(screen.queryByText("Task Details")).not.toBeInTheDocument();
    });
  });

  describe("deep linking", () => {
    it("deep link to /sessions/:machineId/:id renders correct session", async () => {
      render(<AppRoutes initialEntries={["/sessions/machine-1/sess-1"]} />);

      expect(await screen.findByText("sess-1")).toBeInTheDocument();
      expect(screen.getByText("machine-1")).toBeInTheDocument();
    });

    it("deep link to /tasks/:queueName/:id renders correct task", async () => {
      render(<AppRoutes initialEntries={["/tasks/default/task-1"]} />);

      expect(await screen.findByText("task-1")).toBeInTheDocument();
      expect(await screen.findByText("Build project")).toBeInTheDocument();
    });

    it("deep link with encoded path segments works", async () => {
      render(<AppRoutes initialEntries={["/sessions/machine%201/sess-1"]} />);

      // Should still render SessionDetail (the component gets the decoded param)
      expect(await screen.findByText("Session Details")).toBeInTheDocument();
    });
  });

  describe("navigation between routes", () => {
    it("navigating from Dashboard to Sessions works", async () => {
      const user = userEvent.setup();
      render(<AppRoutes initialEntries={["/"]} />);

      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();

      await user.click(screen.getByRole("link", { name: "Sessions" }));

      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("navigating from Sessions back to Dashboard works", async () => {
      const user = userEvent.setup();
      render(<AppRoutes initialEntries={["/sessions"]} />);

      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();

      await user.click(screen.getByRole("link", { name: "Dashboard" }));

      expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    });

    it("navigating from SessionDetail back to sessions list works", async () => {
      const user = userEvent.setup();
      render(<AppRoutes initialEntries={["/sessions/machine-1/sess-1"]} />);

      const backLink = await screen.findByText(/Back to Sessions/);
      await user.click(backLink);

      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });

    it("navigating from TaskDetail back to sessions list works", async () => {
      const user = userEvent.setup();
      render(<AppRoutes initialEntries={["/tasks/default/task-1"]} />);

      const backLink = await screen.findByText(/Back to Sessions/);
      await user.click(backLink);

      expect(await screen.findByRole("heading", { name: "Sessions" })).toBeInTheDocument();
    });
  });
});
