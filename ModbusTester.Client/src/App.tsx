import { useCallback, useEffect, useRef, useState } from "react";
import { AppHeader } from "./components/AppHeader";
import { DeviceSidebar } from "./components/DeviceSidebar";
import { ControlPanel } from "./components/ControlPanel";
import { DataGrid } from "./components/DataGrid";
import { SystemLog, type LogEntry, type LogLevel } from "./components/SystemLog";
import { TrafficLogger } from "./components/TrafficLogger";
import { useModbusSession } from "./hooks/useModbusSession";
import { createSession, getErrorMessage, stopSession, updateParameters, writeCoil, writeRegister } from "./lib/apiClient";
import { createDefaultParameters, type DeviceEntry } from "./types/device";
import { isBitFunctionCode, type PollingParameters } from "./types/modbus";

function phaseToLogLevel(phase: DeviceEntry["phase"]): LogLevel {
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

function App() {
  const [devices, setDevices] = useState<DeviceEntry[]>([]);
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [isTrafficDrawerOpen, setIsTrafficDrawerOpen] = useState(false);
  const [logEntries, setLogEntries] = useState<LogEntry[]>([]);
  const logIdRef = useRef(0);

  const activeDevice = devices.find((d) => d.sessionId === activeSessionId) ?? null;
  const live = useModbusSession(activeSessionId);

  const pushLog = useCallback((level: LogLevel, message: string) => {
    logIdRef.current += 1;
    setLogEntries((prev) => [...prev.slice(-499), { id: logIdRef.current, timestamp: Date.now(), level, message }]);
  }, []);

  // Mirror the live phase back onto the sidebar entry so its status dot stays in sync.
  useEffect(() => {
    if (!activeSessionId) return;
    setDevices((prev) => prev.map((d) => (d.sessionId === activeSessionId ? { ...d, phase: live.phase } : d)));
  }, [activeSessionId, live.phase]);

  useEffect(() => {
    if (!live.statusMessage) return;
    pushLog(phaseToLogLevel(live.phase), live.statusMessage);
    // Only re-run when the message itself changes, not on every phase/pushLog identity change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [live.statusMessage]);

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

  function handleParametersChange(next: PollingParameters) {
    if (!activeDevice) return;
    setDevices((prev) => prev.map((d) => (d.sessionId === activeDevice.sessionId ? { ...d, parameters: next } : d)));

    // IP/Port are locked in the UI while a session is running (see ControlPanel); every other
    // field is safe to push straight through to the already-running driver loop.
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
      pushLog("success", `Wrote ${value} to register ${address}.`);
    } catch (err) {
      pushLog("error", `Write failed: ${getErrorMessage(err)}`);
    }
  }

  async function handleWriteCoil(address: number, value: boolean) {
    if (!activeDevice) return;
    try {
      await writeCoil(activeDevice.sessionId, activeDevice.parameters.slaveId, address, value);
      pushLog("success", `Wrote ${value} to coil ${address}.`);
    } catch (err) {
      pushLog("error", `Write failed: ${getErrorMessage(err)}`);
    }
  }

  const isBitBased = activeDevice ? isBitFunctionCode(activeDevice.parameters.functionCode) : false;

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
                isBitBased={isBitBased}
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
