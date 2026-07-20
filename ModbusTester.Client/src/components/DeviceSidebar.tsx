import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { cn } from "../lib/cn";
import { StatusDot } from "./StatusDot";
import type { DeviceEntry } from "../types/device";

interface DeviceSidebarProps {
  devices: DeviceEntry[];
  activeSessionId: string | null;
  onSelect: (sessionId: string) => void;
  onAdd: () => void;
  onDelete: (sessionId: string) => void;
  onRename: (sessionId: string, label: string) => void;
}

export function DeviceSidebar({ devices, activeSessionId, onSelect, onAdd, onDelete, onRename }: DeviceSidebarProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState("");

  function startEditing(device: DeviceEntry) {
    setEditingId(device.sessionId);
    setEditValue(device.label);
  }

  function commitEditing() {
    if (editingId) {
      const trimmed = editValue.trim();
      if (trimmed !== "") onRename(editingId, trimmed);
    }
    setEditingId(null);
  }

  function handleDelete(e: React.MouseEvent, device: DeviceEntry) {
    e.stopPropagation();
    if (window.confirm(`"${device.label}" cihazını silmek istediğinize emin misiniz?`)) {
      onDelete(device.sessionId);
    }
  }

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
          const isEditing = editingId === device.sessionId;
          return (
            <div
              key={device.sessionId}
              onClick={() => !isEditing && onSelect(device.sessionId)}
              className={cn(
                "group relative flex h-9 cursor-pointer items-center rounded px-[10px] text-left text-[13px] transition-colors",
                isActive ? "bg-surface-hover text-foreground" : "text-muted-foreground hover:bg-surface-hover hover:text-foreground",
              )}
            >
              {isActive && <span className="absolute inset-y-0 left-0 w-[2px] rounded-l bg-accent" />}
              <StatusDot phase={device.phase} className="mr-3 flex-shrink-0" />

              {isEditing ? (
                <input
                  autoFocus
                  value={editValue}
                  onChange={(e) => setEditValue(e.target.value)}
                  onBlur={commitEditing}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") commitEditing();
                    if (e.key === "Escape") setEditingId(null);
                  }}
                  onClick={(e) => e.stopPropagation()}
                  className="min-w-0 flex-1 border border-accent bg-panel px-1 text-[13px] text-foreground outline-none"
                />
              ) : (
                <span className="min-w-0 flex-1 truncate" onDoubleClick={() => startEditing(device)} title="Double-click to rename">
                  {device.label}
                </span>
              )}

              {!isEditing && (
                <button
                  onClick={(e) => handleDelete(e, device)}
                  title="Delete device"
                  className="ml-1 flex h-6 w-6 flex-shrink-0 items-center justify-center rounded text-muted-foreground opacity-0 transition-colors hover:bg-danger/10 hover:text-danger group-hover:opacity-100"
                >
                  <Trash2 className="h-[13px] w-[13px]" />
                </button>
              )}
            </div>
          );
        })}

        {devices.length === 0 && (
          <p className="px-1 py-2 text-[12px] text-dim-foreground">No devices yet — click + to add one.</p>
        )}
      </div>
    </aside>
  );
}
