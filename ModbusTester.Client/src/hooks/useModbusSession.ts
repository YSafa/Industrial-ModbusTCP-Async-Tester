import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { ensureConnected, getHubConnection } from "../lib/signalr";
import { base64ToHexString } from "../lib/binary";
import type { ConnectionPhase, ModbusDataSnapshot, TrafficEntry } from "../types/modbus";

const MAX_TRAFFIC_ENTRIES = 500;

export interface ModbusSessionLiveState {
  phase: ConnectionPhase;
  statusMessage: string;
  snapshot: ModbusDataSnapshot | null;
  traffic: TrafficEntry[];
  clearTraffic: () => void;
}

/**
 * Joins the SignalR group for `sessionId` (see ModbusHub) and mirrors its three server-pushed
 * events — PhaseChanged, DataReceived, Traffic — into React state. Pass null while no session is
 * selected; the hook does nothing until a real id is supplied.
 */
export function useModbusSession(sessionId: string | null): ModbusSessionLiveState {
  const [phase, setPhase] = useState<ConnectionPhase>("Idle");
  const [statusMessage, setStatusMessage] = useState("");
  const [snapshot, setSnapshot] = useState<ModbusDataSnapshot | null>(null);
  const [traffic, setTraffic] = useState<TrafficEntry[]>([]);
  const trafficIdRef = useRef(0);

  useEffect(() => {
    if (!sessionId) return;

    let cancelled = false;
    const connection = getHubConnection();

    setPhase("Idle");
    setStatusMessage("");
    setSnapshot(null);
    setTraffic([]);

    const onPhaseChanged = (id: string, newPhase: ConnectionPhase, message: string) => {
      if (id !== sessionId) return;
      setPhase(newPhase);
      setStatusMessage(message);
    };

    const onDataReceived = (id: string, data: ModbusDataSnapshot) => {
      if (id !== sessionId) return;
      setSnapshot(data);
    };

    const onTraffic = (id: string, frameBase64: string, isTx: boolean) => {
      if (id !== sessionId) return;
      trafficIdRef.current += 1;
      const entry: TrafficEntry = {
        id: trafficIdRef.current,
        timestamp: Date.now(),
        isTx,
        hex: base64ToHexString(frameBase64),
      };
      setTraffic((prev) => {
        const next = prev.length >= MAX_TRAFFIC_ENTRIES ? prev.slice(1) : prev;
        return [...next, entry];
      });
    };

    connection.on("PhaseChanged", onPhaseChanged);
    connection.on("DataReceived", onDataReceived);
    connection.on("Traffic", onTraffic);

    ensureConnected()
      .then(() => {
        if (!cancelled) return connection.invoke("JoinSession", sessionId);
      })
      .catch((err) => console.error("Failed to join Modbus session group", err));

    return () => {
      cancelled = true;
      connection.off("PhaseChanged", onPhaseChanged);
      connection.off("DataReceived", onDataReceived);
      connection.off("Traffic", onTraffic);
      if (connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke("LeaveSession", sessionId).catch(() => {});
      }
    };
  }, [sessionId]);

  return { phase, statusMessage, snapshot, traffic, clearTraffic: () => setTraffic([]) };
}
