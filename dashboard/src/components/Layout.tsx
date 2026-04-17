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
            <option value="dark">Dark</option>
            <option value="pride-dark">Pride Dark</option>
            <option value="pride-light">Pride Light</option>
            <option value="progress-pride-dark">Progress Pride Dark</option>
            <option value="progress-pride-light">Progress Pride Light</option>
            <option value="trans-pride-dark">Trans Pride Dark</option>
            <option value="trans-pride-light">Trans Pride Light</option>
            <option value="hot-dog-stand">Hot Dog Stand</option>
            <option value="arctic-reflection">Arctic Reflection</option>
            <option value="frosted-aura">Frosted Aura</option>
            <option value="moon-dust">Moon Dust</option>
            <option value="amber-walnut-morning">Amber Walnut Morning</option>
            <option value="sorbet">Sorbet</option>
            <option value="pearl">Pearl</option>
            <option value="jade-pebble-morning">Jade Pebble Morning</option>
            <option value="neutral-elegance">Neutral Elegance</option>
            <option value="ink-wash">Ink Wash</option>
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
