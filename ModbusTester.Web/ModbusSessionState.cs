using ModbusTester.Core.Core;
using ModbusTester.Core.Protocol;

namespace ModbusTester.Web
{
    public enum ConnectionPhase { Idle, Searching, Connected, DataError, Reconnecting }

    /// <summary>
    /// Snapshot of the polling parameters a caller (Hub/Controller) wants applied on the next
    /// tick. Mutated by the caller, read by ModbusSessionManager — never the other way around.
    /// </summary>
    public sealed class PollingParameters
    {
        public string Ip { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;
        public byte SlaveId { get; set; }
        public ushort StartAddress { get; set; }
        public string DataType { get; set; } = "Unsigned (16-bit)";
        public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;
        public int UserQuantity { get; set; } = 1;
        public int IntervalMs { get; set; } = 1000;
    }

    /// <summary>
    /// Pure, in-RAM state for one web-connected session/tab. Holds data only — no UI references,
    /// no I/O, no behavior. Owned exclusively by ModbusSessionManager; Hubs/Controllers only ever
    /// read from or hand a PollingParameters snapshot to the manager, never touch this directly.
    /// </summary>
    public sealed class ModbusSessionState
    {
        public string SessionId { get; }

        public PooledConnection? Connection { get; set; }
        public CancellationTokenSource? DriverCts { get; set; }
        public Task? DriverTask { get; set; }
        public PeriodicTimer? Timer { get; set; }

        /// <summary>
        /// The delegate currently subscribed to Connection.Client.OnTraffic, kept so the manager
        /// can detach it before the connection is released/swapped without leaking handlers onto
        /// a pooled client that may outlive this session.
        /// </summary>
        public Action<byte[], bool>? TrafficHandler { get; set; }

        public string CurrentIp { get; set; } = "127.0.0.1";
        public int CurrentPort { get; set; } = 502;
        public ConnectionPhase CurrentPhase { get; set; } = ConnectionPhase.Idle;

        public PollingParameters Parameters { get; set; } = new();

        public ushort[]? OldValues { get; set; }
        public bool[]? OldBitValues { get; set; }
        public ushort[]? RegisterReadBuffer { get; set; }

        public byte? LastProtocolErrorCode { get; set; }
        public string? LastGeneralErrorMessage { get; set; }
        public DateTime? TimeoutBackoffUntilUtc { get; set; }

        public ushort RenderedStartAddress { get; set; }
        public int RenderedRowCount { get; set; }
        public string RenderedDataType { get; set; } = string.Empty;

        public int ConsecutiveSuccessCount { get; set; }

        public ModbusSessionState(string sessionId)
        {
            SessionId = sessionId;
        }
    }
}
