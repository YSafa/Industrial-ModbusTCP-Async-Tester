import type { ConnectionPhase, PollingParameters } from "./modbus";

/**
 * Client-side view of one sidebar entry. sessionId is generated locally and reused across
 * connect/stop cycles — the backend session with that id only exists while phase !== "Idle".
 */
export interface DeviceEntry {
  sessionId: string;
  label: string;
  phase: ConnectionPhase;
  parameters: PollingParameters;
}

export function createDefaultParameters(ip = "127.0.0.1"): PollingParameters {
  return {
    ip,
    port: 502,
    slaveId: 1,
    startAddress: 0,
    dataType: "Unsigned (16-bit)",
    functionCode: "ReadHoldingRegisters",
    quantity: 10,
    intervalMs: 500,
  };
}
