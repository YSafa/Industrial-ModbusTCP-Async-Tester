import { useCallback, useEffect, useRef, useState } from "react";
import { ChevronDown, ChevronRight, Trash2 } from "lucide-react";
import { cn } from "../lib/cn";

export type LogLevel = "info" | "success" | "warning" | "error";

export interface LogEntry {
  /** crypto.randomUUID() — collision-proof regardless of StrictMode/HMR double-invocation quirks
   * that a plain incrementing ref counter can fall prey to across component re-mounts. */
  id: string;
  timestamp: number;
  level: LogLevel;
  message: string;
}

const LEVEL_COLOR: Record<LogLevel, string> = {
  info: "text-accent",
  success: "text-success",
  warning: "text-warning",
  error: "text-danger",
};

function formatTime(ts: number): string {
  return new Date(ts).toISOString().slice(11, 23);
}

interface SystemLogProps {
  entries: LogEntry[];
  isLive: boolean;
  onClear: () => void;
}

const MIN_HEIGHT = 32;
const DEFAULT_HEIGHT = 180;
/** How close to the bottom (px) counts as "still following the log" for auto-scroll purposes. */
const NEAR_BOTTOM_PX = 48;

export function SystemLog({ entries, isLive, onClear }: SystemLogProps) {
  const [height, setHeight] = useState(DEFAULT_HEIGHT);
  const [collapsed, setCollapsed] = useState(false);
  const dragStart = useRef<{ y: number; height: number } | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  // Defaults to true so the log starts pinned to the bottom; flips to false once the user scrolls
  // up to review history, so new entries don't yank the view back down under them.
  const isNearBottomRef = useRef(true);

  const handleScroll = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    isNearBottomRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < NEAR_BOTTOM_PX;
  }, []);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el || collapsed) return;
    if (isNearBottomRef.current) el.scrollTop = el.scrollHeight;
  }, [entries, collapsed]);

  const onDragMove = useCallback((e: MouseEvent) => {
    if (!dragStart.current) return;
    const delta = dragStart.current.y - e.clientY;
    setHeight(Math.max(MIN_HEIGHT, dragStart.current.height + delta));
  }, []);

  const onDragEnd = useCallback(() => {
    dragStart.current = null;
    window.removeEventListener("mousemove", onDragMove);
    window.removeEventListener("mouseup", onDragEnd);
  }, [onDragMove]);

  function onDragStart(e: React.MouseEvent) {
    dragStart.current = { y: e.clientY, height };
    window.addEventListener("mousemove", onDragMove);
    window.addEventListener("mouseup", onDragEnd);
  }

  // Guards against a leak, not just tidiness: if this component unmounts mid-drag (e.g. the
  // device being viewed gets deleted while the user is resizing the log panel), onDragEnd's
  // mouseup handler never fires, so without this the window-level listeners added in onDragStart
  // would stay attached forever — each one pinning this entire component instance's closures
  // (and everything they reference) out of GC reach for the rest of the page's lifetime.
  useEffect(() => {
    return () => {
      window.removeEventListener("mousemove", onDragMove);
      window.removeEventListener("mouseup", onDragEnd);
    };
  }, [onDragMove, onDragEnd]);

  return (
    <>
      <div
        onMouseDown={onDragStart}
        className="relative z-10 flex h-[10px] w-full flex-shrink-0 cursor-row-resize items-center justify-center border-t border-border bg-panel-alt"
      >
        <div className="h-[6px] w-12 rounded-full bg-scrollbar-thumb transition-colors hover:bg-scrollbar-thumb-hover" />
      </div>

      <div
        className="flex w-full flex-shrink-0 flex-col overflow-hidden bg-panel-alt"
        style={{ height: collapsed ? MIN_HEIGHT : height }}
      >
        <div className="flex h-8 flex-shrink-0 items-center justify-between border-b border-border bg-panel px-[10px]">
          <div className="flex items-center gap-2">
            <button
              onClick={() => setCollapsed((c) => !c)}
              className="rounded p-[2px] text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
            >
              {collapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
            </button>
            <span className="text-[12px] font-medium uppercase tracking-wider text-foreground">System Log</span>
            <span className="ml-2 flex items-center text-[11px] font-medium text-muted-foreground">
              <span className={cn("mr-2 h-[6px] w-[6px] rounded-full", isLive ? "bg-success" : "bg-muted-foreground")} />
              {isLive ? "Logging" : "Ready"}
            </span>
          </div>

          <button
            onClick={onClear}
            className="flex h-7 w-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
          >
            <Trash2 className="h-[14px] w-[14px]" />
          </button>
        </div>

        {!collapsed && (
          <div
            ref={scrollRef}
            onScroll={handleScroll}
            className="scrollbar-industrial flex-1 overflow-y-auto px-[10px] py-[6px] font-mono text-[12px] leading-[1.6]"
          >
            {entries.map((entry) => (
              <div key={entry.id} className="flex items-start">
                <span className="mr-3 w-[85px] shrink-0 text-faint-foreground">{formatTime(entry.timestamp)}</span>
                <span className={cn("mr-3 w-[60px] shrink-0 font-medium uppercase", LEVEL_COLOR[entry.level])}>
                  {entry.level}
                </span>
                <span className={cn(entry.level === "info" ? "text-muted-foreground" : LEVEL_COLOR[entry.level])}>
                  {entry.message}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </>
  );
}
