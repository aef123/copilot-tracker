import { describe, it, expect } from "vitest";
import { msalConfig, loginRequest } from "../msalConfig";

describe("msalConfig", () => {
  it("should have correct client ID", () => {
    expect(msalConfig.auth.clientId).toBe("4c8148f5-c913-40c5-863f-1c019821eac4");
  });

  it("should use session storage for caching", () => {
    expect(msalConfig.cache?.cacheLocation).toBe("sessionStorage");
  });

  it("should target the correct tenant", () => {
    expect(msalConfig.auth.authority).toContain("5df6d88f-0d78-491b-9617-8b43a209ba73");
  });
});

describe("loginRequest", () => {
  it("should request the CopilotTracker.ReadWrite scope", () => {
    expect(loginRequest.scopes).toHaveLength(1);
    expect(loginRequest.scopes[0]).toContain("CopilotTracker.ReadWrite");
  });
});
