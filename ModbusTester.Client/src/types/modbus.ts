/**
 * Mirrors ModbusTester.Web's wire contracts exactly (ModbusSessionState.cs,
 * ModbusSessionManager.cs, SessionsController.cs). Keep in sync by hand — there is no shared
 * schema between the two projects.
 */

export type ConnectionPhase = "Idle" | "Searching" | "Connected" | "DataError" | "Reconnecting";

export type ModbusFunctionCode =
  | "ReadCoils"
  | "ReadDiscreteInputs"
  | "ReadHoldingRegisters"
  | "ReadInputRegisters"
  | "WriteSingleCoil"
  | "WriteSingleRegister"
  | "WriteMultipleCoils"
  | "WriteMultipleRegisters";

export const READ_FUNCTION_CODES: { value: ModbusFunctionCode; label: string }[] = [
  { value: "ReadCoils", label: "01 Read Coils" },
  { value: "ReadDiscreteInputs", label: "02 Read Discrete Inputs" },
  { value: "ReadHoldingRegisters", label: "03 Read Holding Registers" },
  { value: "ReadInputRegisters", label: "04 Read Input Registers" },
];

/** Matches ModbusSessionManager.GetRegisterSizeForDataType's exact string switch. */
export const DATA_TYPES = [
  "Unsigned (16-bit)",
  "Signed (16-bit)",
  "Binary (String)",
  "Long (32-bit)",
  "Long Inverse (32-bit)",
  "Float (32-bit)",
  "Float Inverse (32-bit)",
  "Double (64-bit)",
  "Double Inverse (64-bit)",
] as const;

export type ModbusDataType = (typeof DATA_TYPES)[number];

export function isBitFunctionCode(fc: ModbusFunctionCode): boolean {
  return fc === "ReadCoils" || fc === "ReadDiscreteInputs";
}

export interface PollingParameters {
  ip: string;
  port: number;
  slaveId: number;
  startAddress: number;
  dataType: ModbusDataType;
  functionCode: ModbusFunctionCode;
  quantity: number;
  intervalMs: number;
}

export interface SessionSummary {
  sessionId: string;
  phase: ConnectionPhase;
  ip: string;
  port: number;
}

/** Pushed by ModbusHub's "DataReceived" event; mirrors ModbusDataSnapshot.cs. */
export interface ModbusDataSnapshot {
  dataType: ModbusDataType;
  startAddress: number;
  registerSizePerItem: number;
  registers?: number[];
  bits?: boolean[];
}

export interface TrafficEntry {
  /** crypto.randomUUID() — see LogEntry.id for why this isn't a plain incrementing counter. */
  id: string;
  timestamp: number;
  isTx: boolean;
  hex: string;
}
