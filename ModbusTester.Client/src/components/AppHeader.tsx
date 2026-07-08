import { ActivitySquare, Settings } from "lucide-react";
import { cn } from "../lib/cn";

interface AppHeaderProps {
  isTrafficDrawerOpen: boolean;
  onToggleTrafficDrawer: () => void;
}

export function AppHeader({ isTrafficDrawerOpen, onToggleTrafficDrawer }: AppHeaderProps) {
  return (
    <header className="z-10 flex w-full flex-shrink-0 flex-col border-b border-border bg-panel">
      <div className="flex h-12 items-center justify-between px-4">
        <h1 className="text-[20px] font-semibold tracking-tight text-foreground">
          Industrial ModbusTCP Async Tester
        </h1>

        <div className="flex items-center gap-2">
          <button
            onClick={onToggleTrafficDrawer}
            title="Communication Traffic"
            className={cn(
              "flex h-8 w-8 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground",
              isTrafficDrawerOpen && "bg-surface-hover text-foreground",
            )}
          >
            <ActivitySquare className="h-4 w-4" strokeWidth={1.5} />
          </button>
          <button
            title="Settings"
            className="flex h-8 w-8 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
          >
            <Settings className="h-4 w-4" strokeWidth={1.5} />
          </button>
        </div>
      </div>
    </header>
  );
}
