import type { HealthSummary } from "./types";

// Health endpoint is anonymous - no auth needed
export async function getHealth(): Promise<HealthSummary> {
  const response = await fetch("/api/health");
  if (!response.ok) {
    throw new Error(`Health check failed: ${response.status}`);
  }
  return response.json();
}
