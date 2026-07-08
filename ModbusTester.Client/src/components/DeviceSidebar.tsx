import { Plus } from "lucide-react";
import { cn } from "../lib/cn";
import { StatusDot } from "./StatusDot";
import type { DeviceEntry } from "../types/device";

interface DeviceSidebarProps {
  devices: DeviceEntry[];
  activeSessionId: string | null;
  onSelect: (sessionId: string) => void;
  onAdd: () => void;
}

export function DeviceSidebar({ devices, activeSessionId, onSelect, onAdd }: DeviceSidebarProps) {
  return (
    <aside className="z-0 flex h-full w-[250px] flex-shrink-0 flex-col border-r border-border bg-panel p-3">
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-[15px] font-medium text-muted-foreground">Devices</h2>
        <button
          onClick={onAdd}
          title="Add device"
          className="flex h-7 w-7 items-center justify-center rounded text-muted-foreground transition-colors hover:bg-surface-hover hover:text-foreground"
        >
          <Plus className="h-4 w-4" />
        </button>
      </div>

      <div className="flex flex-col gap-1 overflow-y-auto pr-1 scrollbar-industrial">
        {devices.map((device) => {
          const isActive = device.sessionId === activeSessionId;
          return (
            <button
              key={device.sessionId}
              onClick={() => onSelect(device.sessionId)}
              className={cn(
                "relative flex h-9 items-center rounded px-[10px] text-left text-[13px] transition-colors",
                isActive ? "bg-surface-hover text-foreground" : "text-muted-foreground hover:bg-surface-hover hover:text-foreground",
              )}
            >
              {isActive && <span className="absolute inset-y-0 left-0 w-[2px] rounded-l bg-accent" />}
              <StatusDot phase={device.phase} className="mr-3" />
              <span className="truncate">{device.label}</span>
            </button>
          );
        })}

        {devices.length === 0 && (
          <p className="px-1 py-2 text-[12px] text-dim-foreground">No devices yet — click + to add one.</p>
        )}
      </div>
    </aside>
  );
}
