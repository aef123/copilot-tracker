import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import "./styles.css";

export function Layout() {
  const { logout, user } = useAuth();

  return (
    <div className="layout">
      <header className="layout-header">
        <h1>Copilot Session Tracker</h1>
        <nav className="layout-nav">
          <NavLink to="/" end>
            Dashboard
          </NavLink>
          <NavLink to="/sessions">Sessions</NavLink>
        </nav>
        <div className="layout-user">
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
