import { useState, useEffect } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import "./styles.css";

const THEME_KEY = "copilot-tracker-theme";

export function Layout() {
  const { logout, user } = useAuth();
  const [theme, setTheme] = useState(() => {
    return localStorage.getItem(THEME_KEY) || "purple-dark";
  });

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem(THEME_KEY, theme);
  }, [theme]);

  return (
    <div className="layout">
      <header className="layout-header">
        <h1>Copilot Session Tracker</h1>
        <nav className="layout-nav">
          <NavLink to="/" end>
            Grid
          </NavLink>
          <NavLink to="/sessions">Sessions</NavLink>
          <NavLink to="/analytics">Analytics</NavLink>
        </nav>
        <div className="layout-user">
          <select
            value={theme}
            onChange={(e) => setTheme(e.target.value)}
            className="theme-select"
            aria-label="Select theme"
          >
            <option value="purple-dark">Purple Dark</option>
            <option value="purple-light">Purple Light</option>
            <option value="pride">Pride</option>
            <option value="progress-pride">Progress Pride</option>
            <option value="trans-pride-dark">Trans Pride Dark</option>
          </select>
          <span>{user?.name}</span>
          <button onClick={logout}>Sign Out</button>
        </div>
      </header>
      <main className="layout-content">
        <Outlet />
      </main>
    </div>
  );
}
