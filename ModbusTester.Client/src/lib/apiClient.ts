import axios from "axios";
import type { ModbusFunctionCode, ModbusDataType, PollingParameters, SessionSummary } from "../types/modbus";

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

export interface CreateSessionRequest {
  sessionId?: string;
  ip: string;
  port: number;
  slaveId: number;
  startAddress: number;
  dataType: ModbusDataType;
  functionCode: ModbusFunctionCode;
  quantity: number;
  intervalMs: number;
}

/** POST /api/sessions — create a session and immediately start its driver loop. */
export async function createSession(request: CreateSessionRequest): Promise<SessionSummary> {
  const response = await api.post<SessionSummary>("/api/sessions", request);
  return response.data;
}

/** GET /api/sessions/{id} — current phase/target snapshot. 404 if the session doesn't exist. */
export async function getSession(sessionId: string): Promise<SessionSummary> {
  const response = await api.get<SessionSummary>(`/api/sessions/${encodeURIComponent(sessionId)}`);
  return response.data;
}

/** PUT /api/sessions/{id}/parameters — re-point a running session at a new target/read config. */
export async function updateParameters(sessionId: string, parameters: PollingParameters): Promise<void> {
  await api.put(`/api/sessions/${encodeURIComponent(sessionId)}/parameters`, parameters);
}

/** POST /api/sessions/{id}/stop — cancel the driver loop and release the pooled connection. */
export async function stopSession(sessionId: string): Promise<void> {
  await api.post(`/api/sessions/${encodeURIComponent(sessionId)}/stop`);
}

export async function writeCoil(sessionId: string, slaveId: number, address: number, value: boolean): Promise<void> {
  await api.post(`/api/sessions/${encodeURIComponent(sessionId)}/write/coil`, { slaveId, address, value });
}

export async function writeRegister(sessionId: string, slaveId: number, address: number, value: number): Promise<void> {
  await api.post(`/api/sessions/${encodeURIComponent(sessionId)}/write/register`, { slaveId, address, value });
}

export function getErrorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return (err.response?.data as string | undefined) ?? err.message;
  }
  return err instanceof Error ? err.message : String(err);
}

export async function writeRegisters(
  sessionId: string,
  slaveId: number,
  startAddress: number,
  values: number[],
): Promise<void> {
  await api.post(`/api/sessions/${encodeURIComponent(sessionId)}/write/registers`, {
    slaveId,
    startAddress,
    values,
  });
}
