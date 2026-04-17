import type { Session } from "../api";

export function getDisplayStatus(session: Session): string {
  if (session.status === "stale") return "Stale";
  if (session.status === "closed") return "Closed";
  if (session.hasActivePrompt) return "Responding";
  return "Idle";
}

export function getTitleColorClass(session: Session): string {
  if (session.hasActivePrompt) return "title-responding";
  if (session.status === "active" || session.status === "idle") return "title-idle";
  return "";
}
