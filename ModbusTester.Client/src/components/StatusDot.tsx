import { cn } from "../lib/cn";
import type { ConnectionPhase } from "../types/modbus";

const PHASE_COLOR: Record<ConnectionPhase, string> = {
  Connected: "bg-success",
  Searching: "bg-warning",
  Reconnecting: "bg-warning",
  DataError: "bg-danger",
  Idle: "bg-dim-foreground",
};

export function StatusDot({ phase, className }: { phase: ConnectionPhase; className?: string }) {
  const color = PHASE_COLOR[phase];
  return (
    <span className={cn("relative inline-flex h-2 w-2 flex-shrink-0 rounded-full", className)}>
      <span className={cn("absolute inset-0 rounded-full opacity-60 blur-[2px]", color)} />
      <span className={cn("absolute inset-0 rounded-full", color)} />
    </span>
  );
}
