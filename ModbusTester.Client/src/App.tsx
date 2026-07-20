import { useCallback, useMemo, useRef, useState } from "react";
import { AppHeader } from "./components/AppHeader";
import { DeviceSidebar } from "./components/DeviceSidebar";
import { ControlPanel } from "./components/ControlPanel";
import { DataGrid } from "./components/DataGrid";
import { SystemLog, type LogEntry, type LogLevel } from "./components/SystemLog";
import { TrafficLogger } from "./components/TrafficLogger";
import { useModbusSession } from "./hooks/useModbusSession";
import { createSession, getErrorMessage, stopSession, updateParameters, writeCoil, writeRegister } from "./lib/apiClient";
import { toDisplayAddress } from "./lib/modbusAddressing";
import { createDefaultParameters, type DeviceEntry } from "./types/device";
import type { ConnectionPhase, PollingParameters } from "./types/modbus";

function phaseToLogLevel(phase: ConnectionPhase): LogLevel {
  switch (phase) {
    case "Connected":
      return "success";
    case "DataError":
      return "error";
    case "Searching":
    case "Reconnecting":
      return "warning";
    default:
      return "info";
  }
}

/** Best-effort classification of ModbusSessionManager's free-text log lines (see OnLog in ModbusSessionManager.cs). */
function classifyBackendLog(message: string): LogLevel {
  const lower = message.toLowerCase();
  if (lower.includes("error") || lower.includes("could not connect") || lower.includes("exhausted")) return "error";
  if (lower.includes("reconnect") || lower.includes("timeout") || lower.includes("attempt")) return "warning";
  return "info";
}

function App() {
  const [devices, setDevices] = useState<DeviceEntry[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [isTrafficDrawerOpen, setIsTrafficDrawerOpen] = useState(false);
  const [logEntries, setLogEntries] = useState<LogEntry[]>([]);
  const logIdRef = useRef(0);

  const activeDevice = devices.find((d) => d.sessionId === activeSessionId) ?? null;
  const devicesRef = useRef<DeviceEntry[]>(devices);
  devicesRef.current = devices;

  const pushLog = useCallback((level: LogLevel, message: string) => {
    logIdRef.current += 1;
    setLogEntries((prev) => [...prev.slice(-499), { id: logIdRef.current, timestamp: Date.now(), level, message }]);
  }, []);

  const deviceLabel = useCallback(
    (sessionId: string) => devicesRef.current.find((d) => d.sessionId === sessionId)?.label ?? sessionId,
    [],
  );

  // Every device's phase/log events are pushed here regardless of which one is currently being
  // viewed (see useModbusSession) — otherwise a backgrounded device's sidebar dot goes stale.
  const handlePhaseChanged = useCallback(
    (sessionId: string, phase: ConnectionPhase, message: string) => {
      setDevices((prev) => prev.map((d) => (d.sessionId === sessionId ? { ...d, phase } : d)));
      pushLog(phaseToLogLevel(phase), `[${deviceLabel(sessionId)}] ${message}`);
    },
    [pushLog, deviceLabel],
  );

  const handleBackendLog = useCallback(
    (sessionId: string, message: string) => {
      pushLog(classifyBackendLog(message), `[${deviceLabel(sessionId)}] ${message}`);
    },
    [pushLog, deviceLabel],
  );

  const sessionIds = useMemo(() => devices.map((d) => d.sessionId), [devices]);

  const live = useModbusSession({
    sessionIds,
    activeSessionId,
    onPhaseChanged: handlePhaseChanged,
    onLog: handleBackendLog,
  });

  function handleAddDevice() {
    const sessionId = crypto.randomUUID();
    const device: DeviceEntry = {
      sessionId,
      label: `Device ${devices.length + 1}`,
      phase: "Idle",
      parameters: createDefaultParameters(),
    };
    setDevices((prev) => [...prev, device]);
    setActiveSessionId(sessionId);
    pushLog("info", `Added ${device.label}.`);
  }

  function handleDeleteDevice(sessionId: string) {
    const device = devices.find((d) => d.sessionId === sessionId);
    if (!device) return;

    if (device.phase !== "Idle") {
      stopSession(sessionId).catch((err) =>
        pushLog("error", `Failed to stop session before delete: ${getErrorMessage(err)}`),
      );
    }
    live.leaveSession(sessionId);

    setDevices((prev) => prev.filter((d) => d.sessionId !== sessionId));
    setActiveSessionId((prev) => {
      if (prev !== sessionId) return prev;
      const remaining = devices.filter((d) => d.sessionId !== sessionId);
      return remaining[0]?.sessionId ?? null;
    });
    pushLog("info", `Removed ${device.label}.`);
  }

  function handleRenameDevice(sessionId: string, label: string) {
    setDevices((prev) => prev.map((d) => (d.sessionId === sessionId ? { ...d, label } : d)));
  }

  function handleParametersChange(next: PollingParameters) {
    if (!activeDevice) return;
    setDevices((prev) => prev.map((d) => (d.sessionId === activeDevice.sessionId ? { ...d, parameters: next } : d)));

    // Every field except Polling Rate is locked in the UI while a session is running (see
    // ControlPanel), so in practice this only ever reaches the backend for interval changes.
    if (activeDevice.phase !== "Idle") {
      updateParameters(activeDevice.sessionId, next).catch((err) =>
        pushLog("error", `Failed to update parameters: ${getErrorMessage(err)}`),
      );
    }
  }

  async function handleConnect() {
    if (!activeDevice) return;
    try {
      await createSession({ sessionId: activeDevice.sessionId, ...activeDevice.parameters });
      pushLog("info", `Connecting to ${activeDevice.parameters.ip}:${activeDevice.parameters.port}...`);
    } catch (err) {
      pushLog("error", `Failed to start session: ${getErrorMessage(err)}`);
    }
  }

  async function handleStop() {
    if (!activeDevice) return;
    try {
      await stopSession(activeDevice.sessionId);
      pushLog("info", `Stopped ${activeDevice.label}.`);
    } catch (err) {
      pushLog("error", `Failed to stop session: ${getErrorMessage(err)}`);
    }
  }

  async function handleWriteRegister(address: number, value: number) {
    if (!activeDevice) return;
    try {
      await writeRegister(activeDevice.sessionId, activeDevice.parameters.slaveId, address, value);
      pushLog("success", `Wrote ${value} to register ${toDisplayAddress(address, activeDevice.parameters.functionCode)}.`);
    } catch (err) {
      pushLog("error", `Write failed: ${getErrorMessage(err)}`);
    }
  }

  async function handleWriteCoil(address: number, value: boolean) {
    if (!activeDevice) return;
    try {
      await writeCoil(activeDevice.sessionId, activeDevice.parameters.slaveId, address, value);
      pushLog("success", `Wrote ${value} to coil ${toDisplayAddress(address, activeDevice.parameters.functionCode)}.`);
    } catch (err) {
      pushLog("error", `Write failed: ${getErrorMessage(err)}`);
    }
  }

  return (
    <div className="relative flex h-screen w-full flex-col overflow-hidden bg-background text-foreground selection:bg-accent selection:text-white">
      <AppHeader
        isTrafficDrawerOpen={isTrafficDrawerOpen}
        onToggleTrafficDrawer={() => setIsTrafficDrawerOpen((open) => !open)}
      />

      <div className="relative flex w-full flex-1 overflow-hidden">
        <DeviceSidebar
          devices={devices}
          activeSessionId={activeSessionId}
          onSelect={setActiveSessionId}
          onAdd={handleAddDevice}
          onDelete={handleDeleteDevice}
          onRename={handleRenameDevice}
        />

        <main className="z-0 flex h-full min-w-0 flex-1 flex-col">
          {activeDevice ? (
            <>
              <ControlPanel
                parameters={activeDevice.parameters}
                phase={activeDevice.phase}
                onParametersChange={handleParametersChange}
                onConnect={handleConnect}
                onStop={handleStop}
              />
              <DataGrid
                snapshot={live.snapshot}
                functionCode={activeDevice.parameters.functionCode}
                onWriteRegister={handleWriteRegister}
                onWriteCoil={handleWriteCoil}
              />
              <SystemLog
                entries={logEntries}
                isLive={activeDevice.phase === "Connected"}
                onClear={() => setLogEntries([])}
              />
            </>
          ) : (
            <div className="flex flex-1 items-center justify-center text-[13px] text-dim-foreground">
              Add a device to get started.
            </div>
          )}
        </main>

        <TrafficLogger
          isOpen={isTrafficDrawerOpen}
          onClose={() => setIsTrafficDrawerOpen(false)}
          entries={live.traffic}
          onClear={live.clearTraffic}
        />
      </div>
    </div>
  );
}

export default App;
