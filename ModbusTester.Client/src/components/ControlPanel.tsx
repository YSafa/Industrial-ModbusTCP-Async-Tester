import { Loader2, Play, Square } from "lucide-react";
import { cn } from "../lib/cn";
import { DATA_TYPES, READ_FUNCTION_CODES } from "../types/modbus";
import type { ConnectionPhase, PollingParameters } from "../types/modbus";

interface ControlPanelProps {
  parameters: PollingParameters;
  phase: ConnectionPhase;
  onParametersChange: (next: PollingParameters) => void;
  onConnect: () => void;
  onStop: () => void;
}

const PHASE_BUTTON: Record<ConnectionPhase, { label: string; className: string }> = {
  Idle: { label: "Connect", className: "bg-success hover:bg-success/90" },
  Searching: { label: "Connecting...", className: "bg-warning hover:bg-warning/90" },
  Reconnecting: { label: "Reconnecting...", className: "bg-warning hover:bg-warning/90" },
  Connected: { label: "Disconnect", className: "bg-danger hover:bg-danger/90" },
  DataError: { label: "Disconnect", className: "bg-danger hover:bg-danger/90" },
};

function RibbonField({ label, className, children }: { label: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={cn("flex flex-col gap-[2px]", className)}>
      <label className="px-1 text-[10px] font-medium uppercase leading-none text-muted-foreground">{label}</label>
      {children}
    </div>
  );
}

const inputClass =
  "h-[30px] rounded border border-border bg-surface-hover px-2 text-[13px] text-foreground transition-colors focus:border-accent focus:outline-none disabled:opacity-50";

export function ControlPanel({ parameters, phase, onParametersChange, onConnect, onStop }: ControlPanelProps) {
  const isIdle = phase === "Idle";
  const button = PHASE_BUTTON[phase];
  const isBitBased = parameters.functionCode === "ReadCoils" || parameters.functionCode === "ReadDiscreteInputs";

  function set<K extends keyof PollingParameters>(key: K, value: PollingParameters[K]) {
    onParametersChange({ ...parameters, [key]: value });
  }

  return (
    <div className="flex h-12 w-full flex-shrink-0 items-center gap-[6px] border-b border-border bg-panel px-[10px]">
      <RibbonField label="IP Address">
        <input
          type="text"
          value={parameters.ip}
          disabled={!isIdle}
          onChange={(e) => set("ip", e.target.value)}
          className={cn(inputClass, "w-[150px]")}
        />
      </RibbonField>

      <RibbonField label="Port">
        <input
          type="number"
          value={parameters.port}
          disabled={!isIdle}
          onChange={(e) => set("port", Number(e.target.value))}
          className={cn(inputClass, "w-[70px]")}
        />
      </RibbonField>

      <RibbonField label="Slave ID">
        <input
          type="number"
          value={parameters.slaveId}
          onChange={(e) => set("slaveId", Number(e.target.value))}
          className={cn(inputClass, "w-[60px]")}
        />
      </RibbonField>

      <RibbonField label="Function Code">
        <select
          value={parameters.functionCode}
          onChange={(e) => set("functionCode", e.target.value as PollingParameters["functionCode"])}
          className={cn(inputClass, "w-[210px] cursor-pointer")}
        >
          {READ_FUNCTION_CODES.map((fc) => (
            <option key={fc.value} value={fc.value}>
              {fc.label}
            </option>
          ))}
        </select>
      </RibbonField>

      <RibbonField label="Start Address">
        <input
          type="number"
          value={parameters.startAddress}
          onChange={(e) => set("startAddress", Number(e.target.value))}
          className={cn(inputClass, "w-[90px] font-mono")}
        />
      </RibbonField>

      <RibbonField label="Quantity">
        <input
          type="number"
          value={parameters.quantity}
          onChange={(e) => set("quantity", Number(e.target.value))}
          className={cn(inputClass, "w-[70px]")}
        />
      </RibbonField>

      {!isBitBased && (
        <RibbonField label="Data Type">
          <select
            value={parameters.dataType}
            onChange={(e) => set("dataType", e.target.value as PollingParameters["dataType"])}
            className={cn(inputClass, "w-[190px] cursor-pointer")}
          >
            {DATA_TYPES.map((dt) => (
              <option key={dt} value={dt}>
                {dt}
              </option>
            ))}
          </select>
        </RibbonField>
      )}

      <RibbonField label="Polling Rate">
        <div className={cn(inputClass, "flex w-[70px] items-center overflow-hidden pr-1")}>
          <input
            type="number"
            value={parameters.intervalMs}
            onChange={(e) => set("intervalMs", Number(e.target.value))}
            className="w-full min-w-0 bg-transparent text-right text-[13px] text-foreground outline-none"
          />
          <span className="flex-shrink-0 text-[11px] font-medium text-dim-foreground">ms</span>
        </div>
      </RibbonField>

      <div className="flex-1" />

      <button
        onClick={isIdle ? onConnect : onStop}
        className={cn(
          "ml-2 mt-[14px] flex h-8 items-center justify-center gap-[6px] rounded px-3 text-[13px] font-medium text-white transition-colors",
          button.className,
        )}
      >
        {phase === "Idle" && <Play className="h-[14px] w-[14px] fill-current" />}
        {(phase === "Searching" || phase === "Reconnecting") && <Loader2 className="h-[14px] w-[14px] animate-spin" />}
        {(phase === "Connected" || phase === "DataError") && <Square className="h-[14px] w-[14px] fill-current" />}
        {button.label}
      </button>
    </div>
  );
}
