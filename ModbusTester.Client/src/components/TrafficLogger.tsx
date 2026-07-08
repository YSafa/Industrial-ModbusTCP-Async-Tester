import { Trash2, X } from "lucide-react";
import { cn } from "../lib/cn";
import type { TrafficEntry } from "../types/modbus";

function formatTime(ts: number): string {
  return new Date(ts).toISOString().slice(11, 23);
}

interface TrafficLoggerProps {
  isOpen: boolean;
  onClose: () => void;
  entries: TrafficEntry[];
  onClear: () => void;
}

export function TrafficLogger({ isOpen, onClose, entries, onClear }: TrafficLoggerProps) {
  return (
    <div
      className={cn(
        "absolute right-0 top-0 z-20 flex h-full w-[600px] max-w-full flex-col border-l border-border bg-panel transition-transform duration-300 ease-in-out",
        isOpen ? "translate-x-0 shadow-2xl" : "translate-x-full",
      )}
    >
      <div className="flex h-12 flex-shrink-0 items-center justify-between border-b border-border px-4">
        <h2 className="text-[15px] font-medium text-foreground">Communication Traffic</h2>
        <div className="flex items-center gap-1">
          <button
            onClick={onClear}
            title="Clear traffic"
            className="flex h-8 w-8 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
          >
            <Trash2 className="h-4 w-4" />
          </button>
          <button
            onClick={onClose}
            title="Close"
            className="flex h-8 w-8 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="scrollbar-industrial flex-1 space-y-1 overflow-y-auto bg-black p-4 font-mono text-[12px] leading-[1.6]">
        {entries.length === 0 && <p className="text-faint-foreground">No traffic captured yet.</p>}
        {entries.map((entry) => (
          <div key={entry.id} className="flex items-start">
            <span className="mr-3 w-[85px] shrink-0 text-faint-foreground">{formatTime(entry.timestamp)}</span>
            <span className={cn("mr-3 w-[35px] shrink-0 font-bold", entry.isTx ? "text-tx" : "text-rx")}>
              {entry.isTx ? "TX >" : "RX >"}
            </span>
            <span className="break-all text-muted-foreground">{entry.hex}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
