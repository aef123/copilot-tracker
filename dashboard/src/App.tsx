import { AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";
import { useAuth } from "./auth/useAuth";

function App() {
  const { login, logout, user } = useAuth();

  return (
    <div style={{ padding: "2rem", fontFamily: "system-ui" }}>
      <h1>Copilot Session Tracker</h1>

      <UnauthenticatedTemplate>
        <p>Sign in to view your sessions.</p>
        <button onClick={login}>Sign In</button>
      </UnauthenticatedTemplate>

      <AuthenticatedTemplate>
        <p>Welcome, {user?.name}!</p>
        <p style={{ color: "#666", fontSize: "0.875rem" }}>{user?.email}</p>
        <button onClick={logout}>Sign Out</button>
        <hr />
        <p>Dashboard components coming soon...</p>
      </AuthenticatedTemplate>
    </div>
  );
}

export default App;
