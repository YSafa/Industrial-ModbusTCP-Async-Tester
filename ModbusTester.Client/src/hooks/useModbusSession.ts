import { useCallback, useEffect, useRef, useState } from "react";
import { ensureConnected, getHubConnection } from "../lib/signalr";
import { base64ToHexString } from "../lib/binary";
import type { ConnectionPhase, ModbusDataSnapshot, TrafficEntry } from "../types/modbus";

const MAX_TRAFFIC_ENTRIES = 500;

export interface ModbusHubState {
  snapshot: ModbusDataSnapshot | null;
  traffic: TrafficEntry[];
  clearTraffic: () => void;
  /** Leaves the session's SignalR group and drops its cached snapshot (used when a device is deleted). */
  leaveSession: (sessionId: string) => void;
}

interface UseModbusSessionOptions {
  /** Every known device's sessionId — each one's SignalR group is joined as soon as it appears. */
  sessionIds: string[];
  /** Which device's snapshot/traffic should be mirrored into this hook's returned state. */
  activeSessionId: string | null;
  onPhaseChanged: (sessionId: string, phase: ConnectionPhase, message: string) => void;
  onLog: (sessionId: string, message: string) => void;
}

/**
 * One global SignalR subscription for the whole app. Every device's group is joined the moment
 * its sessionId is known and never left — a backend session can be stopped and restarted under
 * the same id, and background devices must keep receiving PhaseChanged/Log pushes even while a
 * different device is the one being viewed (otherwise the sidebar's status dot for a backgrounded
 * device freezes/resets the next time it's reselected, since a fresh mount used to reset local
 * state to Idle before the next real event arrived). Every device's latest snapshot is cached by
 * sessionId and kept up to date even while backgrounded, so switching the active device shows its
 * current data immediately instead of blanking to "Waiting for data..." until the next poll tick.
 * Traffic is still scoped to the active device only (the drawer is a live packet view, not a
 * per-device history) — phase and log events are reported upward via callbacks so the caller can
 * update every device's sidebar entry and a single shared log.
 */
export function useModbusSession({ sessionIds, activeSessionId, onPhaseChanged, onLog }: UseModbusSessionOptions): ModbusHubState {
  const [snapshots, setSnapshots] = useState<Record<string, ModbusDataSnapshot>>({});
  const [traffic, setTraffic] = useState<TrafficEntry[]>([]);

  const activeSessionIdRef = useRef(activeSessionId);
  activeSessionIdRef.current = activeSessionId;

  const onPhaseChangedRef = useRef(onPhaseChanged);
  onPhaseChangedRef.current = onPhaseChanged;

  const onLogRef = useRef(onLog);
  onLogRef.current = onLog;

  // The traffic drawer only ever shows the active device's packets, so clear it on selection
  // change; PhaseChanged/Log/data keep flowing for every device regardless (see the effect below).
  useEffect(() => {
    setTraffic([]);
  }, [activeSessionId]);

  const joinedRef = useRef<Set<string>>(new Set());
  useEffect(() => {
    const newIds = sessionIds.filter((id) => !joinedRef.current.has(id));
    if (newIds.length === 0) return;

    const connection = getHubConnection();
    ensureConnected()
      .then(() => Promise.all(newIds.map((id) => connection.invoke("JoinSession", id))))
      .then(() => {
        newIds.forEach((id) => joinedRef.current.add(id));
      })
      .catch((err) => console.error("Failed to join Modbus session group", err));
  }, [sessionIds]);

  useEffect(() => {
    const connection = getHubConnection();

    const onPhase = (id: string, phase: ConnectionPhase, message: string) => {
      onPhaseChangedRef.current(id, phase, message);
    };

    const onData = (id: string, data: ModbusDataSnapshot) => {
      setSnapshots((prev) => ({ ...prev, [id]: data }));
    };

    const onTraffic = (id: string, frameBase64: string, isTx: boolean) => {
      if (id !== activeSessionIdRef.current) return;
      const entry: TrafficEntry = {
        id: crypto.randomUUID(),
        timestamp: Date.now(),
        isTx,
        hex: base64ToHexString(frameBase64),
      };
      setTraffic((prev) => {
        const next = prev.length >= MAX_TRAFFIC_ENTRIES ? prev.slice(1) : prev;
        return [...next, entry];
      });
    };

    const onLogEvent = (id: string, message: string) => {
      onLogRef.current(id, message);
    };

    connection.on("PhaseChanged", onPhase);
    connection.on("DataReceived", onData);
    connection.on("Traffic", onTraffic);
    connection.on("Log", onLogEvent);

    ensureConnected().catch((err) => console.error("Failed to start Modbus hub connection", err));

    return () => {
      connection.off("PhaseChanged", onPhase);
      connection.off("DataReceived", onData);
      connection.off("Traffic", onTraffic);
      connection.off("Log", onLogEvent);
    };
  }, []);

  const leaveSession = useCallback((sessionId: string) => {
    joinedRef.current.delete(sessionId);
    setSnapshots((prev) => {
      if (!(sessionId in prev)) return prev;
      const next = { ...prev };
      delete next[sessionId];
      return next;
    });
    ensureConnected()
      .then(() => getHubConnection().invoke("LeaveSession", sessionId))
      .catch((err) => console.error("Failed to leave Modbus session group", err));
  }, []);

  const snapshot = (activeSessionId && snapshots[activeSessionId]) || null;

  return { snapshot, traffic, clearTraffic: () => setTraffic([]), leaveSession };
}
