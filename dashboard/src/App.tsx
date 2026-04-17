import { AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { useAuth } from "./auth/useAuth";
import { Layout } from "./components/Layout";
import { HealthDashboard } from "./components/HealthDashboard";
import { SessionList } from "./components/SessionList";
import { SessionGrid } from "./components/SessionGrid";
import { SessionDetail } from "./components/SessionDetail";
import { TaskDetail } from "./components/TaskDetail";
import { PromptDetail } from "./components/PromptDetail";
import { ChartsDashboard } from "./components/ChartsDashboard";
import "./components/styles.css";

function App() {
  const { login } = useAuth();

  return (
    <>
      <UnauthenticatedTemplate>
        <div className="login-page">
          <h1>Copilot Session Tracker</h1>
          <p>Sign in to view your sessions.</p>
          <button className="btn-primary" onClick={login}>
            Sign In
          </button>
        </div>
      </UnauthenticatedTemplate>

      <AuthenticatedTemplate>
        <BrowserRouter>
          <Routes>
            <Route element={<Layout />}>
              <Route path="/" element={<HealthDashboard />} />
              <Route path="/sessions" element={<SessionList />} />
              <Route path="/sessions/grid" element={<SessionGrid />} />
              <Route path="/sessions/:machineId/:id" element={<SessionDetail />} />
              <Route path="/prompts/:sessionId/:id" element={<PromptDetail />} />
              <Route path="/tasks/:queueName/:id" element={<TaskDetail />} />
              <Route path="/analytics" element={<ChartsDashboard />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </AuthenticatedTemplate>
    </>
  );
}

export default App;
