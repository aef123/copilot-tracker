import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Layout } from "../Layout";

const mockLogout = vi.fn();

vi.mock("../../auth/useAuth", () => ({
  useAuth: vi.fn(() => ({
    logout: mockLogout,
    user: { name: "Test User", email: "test@example.com", id: "u1" },
  })),
}));

import { useAuth } from "../../auth/useAuth";

function renderLayout(initialRoute = "/") {
  return render(
    <MemoryRouter initialEntries={[initialRoute]}>
      <Layout />
    </MemoryRouter>
  );
}

describe("Layout", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the app title", () => {
    renderLayout();
    expect(screen.getByText("Copilot Session Tracker")).toBeInTheDocument();
  });

  it("renders navigation links", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sessions" })).toBeInTheDocument();
  });

  it("displays the user name", () => {
    renderLayout();
    expect(screen.getByText("Test User")).toBeInTheDocument();
  });

  it("calls logout when Sign Out is clicked", async () => {
    renderLayout();

    await userEvent.click(screen.getByRole("button", { name: "Sign Out" }));

    expect(mockLogout).toHaveBeenCalledOnce();
  });

  it("renders when user is null", () => {
    vi.mocked(useAuth).mockReturnValue({
      logout: mockLogout,
      user: null,
      isAuthenticated: false,
      login: vi.fn(),
      getAccessToken: vi.fn(),
    });

    renderLayout();
    expect(screen.getByText("Copilot Session Tracker")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign Out" })).toBeInTheDocument();
  });

  it("Dashboard link points to root", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute("href", "/");
  });

  it("Sessions link points to /sessions", () => {
    renderLayout();
    expect(screen.getByRole("link", { name: "Sessions" })).toHaveAttribute("href", "/sessions");
  });
});
