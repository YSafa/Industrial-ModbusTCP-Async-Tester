import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "./apiClient";

/**
 * One HubConnection shared by every useModbusSession() instance in the app — sessions are
 * multiplexed over SignalR groups (see ModbusHub.JoinSession), not separate sockets.
 */
let connection: signalR.HubConnection | null = null;
let startPromise: Promise<void> | null = null;

function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/modbus`)
      .withAutomaticReconnect()
      .build();
  }
  return connection;
}

/** Idempotent: concurrent callers (multiple mounted hooks) share the same in-flight start(). */
export function ensureConnected(): Promise<void> {
  const conn = getConnection();
  if (conn.state === signalR.HubConnectionState.Connected) return Promise.resolve();

  if (!startPromise) {
    startPromise = conn.start().catch((err) => {
      startPromise = null;
      throw err;
    });
  }
  return startPromise;
}

export function getHubConnection(): signalR.HubConnection {
  return getConnection();
}
