using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

namespace ModbusTester.Web
{
    /// <summary>
    /// Immutable snapshot of a completed read, handed to OnDataReceived. RegisterSizePerItem lets
    /// a client decode Registers into typed values (Long/Float/Double) without duplicating the
    /// data-type-to-register-width mapping.
    /// </summary>
    public sealed class ModbusDataSnapshot
    {
        public required string DataType { get; init; }
        public required ushort StartAddress { get; init; }
        public required int RegisterSizePerItem { get; init; }
        public ushort[]? Registers { get; init; }
        public bool[]? Bits { get; init; }
    }

    /// <summary>
    /// Singleton engine owning every web-connected session's polling lifecycle. Hubs/Controllers
    /// are ephemeral callers: they start/stop sessions and push parameter updates, but never touch
    /// ModbusConnectionManager or a ModbusClient directly — this class is the sole owner of the
    /// AcquireAsync/Release lifecycle for every session it runs.
    /// </summary>
    public sealed class ModbusSessionManager : BackgroundService
    {
        private const int ReconnectMaxAttempts = 5;
        private const int ReconnectDelayMs = 2000;
        private const int TimeoutBackoffMs = 3000;
        private const int ProbeTimeoutMs = 300;
        private const int RequiredConsecutiveSuccesses = 3;

        private readonly ConcurrentDictionary<string, ModbusSessionState> _sessions = new();

        /// <summary>sessionId, new phase, human-readable status message.</summary>
        public event Action<string, ConnectionPhase, string>? OnPhaseChanged;

        /// <summary>sessionId, the data that was just read.</summary>
        public event Action<string, ModbusDataSnapshot>? OnDataReceived;

        /// <summary>sessionId, log line (replaces the WinForms color-coded RichTextBox log).</summary>
        public event Action<string, string>? OnLog;

        /// <summary>sessionId, raw frame bytes, isTx (true = request, false = response).</summary>
        public event Action<string, byte[], bool>? OnTraffic;

        public bool TryGetSession(string sessionId, out ModbusSessionState? session)
            => _sessions.TryGetValue(sessionId, out session);

        /// <summary>
        /// Registers a new session and spawns its driver loop. The caller (Hub/Controller) hands
        /// off the connection lifecycle here and does not touch it again until StopSessionAsync.
        /// </summary>
        public Task StartSessionAsync(string sessionId, PollingParameters initialParameters)
        {
            if (_sessions.ContainsKey(sessionId))
                throw new InvalidOperationException($"Session '{sessionId}' is already running.");

            var session = new ModbusSessionState(sessionId) { Parameters = initialParameters };
            _sessions[sessionId] = session;

            var cts = new CancellationTokenSource();
            session.DriverCts = cts;
            session.DriverTask = Task.Run(() => RunDriverAsync(session, cts.Token));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Cancel -> await(join) -> dispose -> release-pooled-connection, mirroring the WinForms
        /// StopDriverAsync teardown sequence exactly.
        /// </summary>
        public async Task StopSessionAsync(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var session)) return;

            session.DriverCts?.Cancel();

            if (session.DriverTask != null && !session.DriverTask.IsCompleted)
            {
                try { await session.DriverTask; }
                catch (OperationCanceledException) { }
            }

            session.DriverCts?.Dispose();
            session.DriverCts = null;
            session.DriverTask = null;

            session.Timer?.Dispose();
            session.Timer = null;

            if (session.Connection != null)
            {
                DetachTraffic(session);
                ModbusConnectionManager.Release(session.CurrentIp, session.CurrentPort);
                session.Connection = null;
            }

            SetPhase(session, ConnectionPhase.Idle, "Not Connected");
        }

        /// <summary>
        /// Replaces the session's live polling target/parameters. Read fresh by the driver loop
        /// on its next tick — no UI thread marshaling needed since callers just swap a reference.
        /// </summary>
        public bool UpdateParameters(string sessionId, PollingParameters parameters)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return false;
            session.Parameters = parameters;
            return true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (string sessionId in _sessions.Keys.ToList())
            {
                await StopSessionAsync(sessionId);
            }

            await base.StopAsync(cancellationToken);
        }

        // ---------------------------------------------------------
        // DUAL-LOOP ARCHITECTURE (Outer / Inner Loop)
        // ---------------------------------------------------------

        private async Task RunDriverAsync(ModbusSessionState session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                bool connected = await EstablishConnectionAsync(session, token);
                if (!connected) break;

                PollingParameters initialParams = session.Parameters;
                bool isBitBased = initialParams.FunctionCode == ModbusFunctionCode.ReadCoils ||
                                  initialParams.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

                session.RegisterReadBuffer = isBitBased
                    ? null
                    : new ushort[initialParams.UserQuantity];

                session.OldValues = null;
                session.OldBitValues = null;
                session.LastProtocolErrorCode = null;
                session.LastGeneralErrorMessage = null;

                PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, initialParams.IntervalMs)));
                session.Timer = timer;

                try
                {
                    await InnerPollingLoopAsync(session, timer, token);
                }
                finally
                {
                    timer.Dispose();
                    session.Timer = null;
                }
            }

            SetPhase(session, ConnectionPhase.Idle, "Not Connected");
        }

        private async Task<bool> EstablishConnectionAsync(ModbusSessionState session, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                string liveIp = session.Parameters.Ip;
                int livePort = session.Parameters.Port;

                // REFCOUNT BALANCE: release any reference already held from a previous trip
                // through this loop (same discipline as the WinForms EstablishConnectionAsync) —
                // AcquireAsync below increments RefCount unconditionally.
                if (session.Connection != null)
                {
                    DetachTraffic(session);
                    ModbusConnectionManager.Release(session.CurrentIp, session.CurrentPort);
                    session.Connection = null;
                }

                session.CurrentIp = liveIp;
                session.CurrentPort = livePort;

                SetPhase(session, ConnectionPhase.Searching, $"Searching for device: {session.CurrentIp}:{session.CurrentPort}...");

                try
                {
                    session.Connection = await ModbusConnectionManager.AcquireAsync(liveIp, livePort, 3000, 3000);
                    AttachTraffic(session);
                    Log(session, "Connected successfully.");
                    SetPhase(session, ConnectionPhase.Connected, "Data Stream Active");
                    return true;
                }
                catch (Exception ex)
                {
                    Log(session, $"Could not connect: {ex.Message} — retrying in 5 sec.");
                }

                try { await Task.Delay(5000, token); }
                catch (OperationCanceledException) { return false; }
            }
            return false;
        }

        private async Task InnerPollingLoopAsync(ModbusSessionState session, PeriodicTimer timer, CancellationToken token)
        {
            int lastIntervalMs = session.Parameters.IntervalMs;

            while (!token.IsCancellationRequested)
            {
                PollingParameters parameters = session.Parameters;

                // Target change guard: if the caller has re-pointed this session at a different
                // IP/Port mid-flight, break back to the outer loop so EstablishConnectionAsync
                // releases the old pooled connection and acquires the new target.
                if (parameters.Ip != session.CurrentIp || parameters.Port != session.CurrentPort)
                {
                    Log(session, $"Target changed to {parameters.Ip}:{parameters.Port} — re-establishing connection.");
                    return;
                }

                // BACKOFF GUARD: skip the read entirely this tick without touching the shared
                // TransactionLock, so a failing slave never starves other sessions sharing the
                // same pooled socket.
                bool inBackoff = session.TimeoutBackoffUntilUtc.HasValue && DateTime.UtcNow < session.TimeoutBackoffUntilUtc.Value;

                if (!inBackoff)
                {
                    try
                    {
                        await ReadAndPublishAsync(session, parameters);
                        session.ConsecutiveSuccessCount++;

                        if (session.ConsecutiveSuccessCount >= RequiredConsecutiveSuccesses)
                        {
                            session.LastProtocolErrorCode = null;
                            session.LastGeneralErrorMessage = null;
                            session.TimeoutBackoffUntilUtc = null;

                            if (session.CurrentPhase != ConnectionPhase.Connected)
                            {
                                SetPhase(session, ConnectionPhase.Connected, "Data Stream Active");
                            }
                        }
                    }
                    catch (ModbusProtocolException ex)
                    {
                        session.ConsecutiveSuccessCount = 0;

                        if (session.LastProtocolErrorCode == null || session.LastProtocolErrorCode.Value != ex.ExceptionCode)
                        {
                            Log(session, $"PROTOCOL ERROR (Code: {ex.ExceptionCode}): {ex.Message}");
                            session.LastProtocolErrorCode = ex.ExceptionCode;
                        }

                        if (session.CurrentPhase != ConnectionPhase.DataError)
                        {
                            SetPhase(session, ConnectionPhase.DataError, $"Invalid Request (Code: {ex.ExceptionCode}) — check Address/Quantity settings");
                        }
                    }
                    catch (ModbusTimeoutException ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;

                        string errorMessage = $"TIMEOUT (Slave ID: {parameters.SlaveId}): {ex.Message}";
                        if (session.LastGeneralErrorMessage != errorMessage)
                        {
                            Log(session, errorMessage);
                            session.LastGeneralErrorMessage = errorMessage;
                        }

                        if (session.Connection != null && session.Connection.Client.IsConnected)
                        {
                            if (session.CurrentPhase != ConnectionPhase.DataError)
                            {
                                SetPhase(session, ConnectionPhase.DataError, $"Device Timeout — Check Slave ID {parameters.SlaveId} status");
                            }

                            session.OldValues = null;
                            session.OldBitValues = null;
                            session.TimeoutBackoffUntilUtc = DateTime.UtcNow.AddMilliseconds(TimeoutBackoffMs);
                        }
                        else
                        {
                            bool reconnected = await TryReconnectAsync(session, token);
                            if (!reconnected) return;
                            session.OldValues = null;
                            session.LastGeneralErrorMessage = null;
                            session.TimeoutBackoffUntilUtc = null;
                        }
                        continue;
                    }
                    catch (ModbusConnectionException ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;
                        Log(session, $"CONNECTION ERROR: {ex.Message}");

                        bool reconnected = await TryReconnectAsync(session, token);
                        if (!reconnected) return;

                        session.OldValues = null;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) break;

                        session.ConsecutiveSuccessCount = 0;

                        if (session.LastGeneralErrorMessage == null || session.LastGeneralErrorMessage != ex.Message)
                        {
                            Log(session, $"ERROR: {ex.Message}");
                            session.LastGeneralErrorMessage = ex.Message;
                        }
                    }
                }

                if (parameters.IntervalMs != lastIntervalMs && parameters.IntervalMs > 0)
                {
                    timer.Dispose();
                    timer = new PeriodicTimer(TimeSpan.FromMilliseconds(parameters.IntervalMs));
                    session.Timer = timer;
                    lastIntervalMs = parameters.IntervalMs;
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(token)) break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<bool> TryReconnectAsync(ModbusSessionState session, CancellationToken token)
        {
            var connection = session.Connection;
            if (connection == null) return false;

            SetPhase(session, ConnectionPhase.Reconnecting, "Connection lost, preparing to reconnect...");

            for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return false;

                if (attempt > 1)
                {
                    try { await Task.Delay(ReconnectDelayMs, token); }
                    catch (OperationCanceledException) { return false; }
                }

                await connection.TransactionLock.WaitAsync(token);
                try
                {
                    if (connection.Client.IsConnected)
                    {
                        SetPhase(session, ConnectionPhase.Connected, "Data Stream Active");
                        Log(session, "Connection already restored by another session sharing this device.");
                        return true;
                    }

                    SetPhase(session, ConnectionPhase.Reconnecting, $"Reconnecting ({attempt}/{ReconnectMaxAttempts})...");
                    await connection.Client.ConnectAsync();

                    SetPhase(session, ConnectionPhase.Connected, "Data Stream Active");
                    Log(session, "Reconnected successfully.");
                    return true;
                }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex)
                {
                    Log(session, $"Attempt {attempt} failed: {ex.Message}");
                }
                finally
                {
                    connection.TransactionLock.Release();
                }
            }

            Log(session, $"{ReconnectMaxAttempts} attempts exhausted, returning to connection phase.");
            return false;
        }

        // ---------------------------------------------------------
        // READ + PUBLISH
        // ---------------------------------------------------------

        private async Task ReadAndPublishAsync(ModbusSessionState session, PollingParameters parameters)
        {
            bool isBitBased = parameters.FunctionCode == ModbusFunctionCode.ReadCoils ||
                               parameters.FunctionCode == ModbusFunctionCode.ReadDiscreteInputs;

            if (isBitBased) await ReadAndPublishBitsAsync(session, parameters);
            else await ReadAndPublishRegistersAsync(session, parameters);
        }

        private async Task ReadAndPublishBitsAsync(ModbusSessionState session, PollingParameters parameters)
        {
            var connection = session.Connection;
            if (connection == null) return;

            int? probeTimeout = session.TimeoutBackoffUntilUtc.HasValue ? ProbeTimeoutMs : (int?)null;

            bool[] bits;

            await connection.TransactionLock.WaitAsync();
            try
            {
                bits = parameters.FunctionCode == ModbusFunctionCode.ReadCoils
                    ? await connection.Client.ReadCoilsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity, probeTimeout)
                    : await connection.Client.ReadDiscreteInputsAsync(parameters.SlaveId, parameters.StartAddress, (ushort)parameters.UserQuantity, probeTimeout);
            }
            finally
            {
                connection.TransactionLock.Release();
            }

            if (HasBitValuesChanged(session.OldBitValues, bits))
            {
                session.OldBitValues = (bool[])bits.Clone();
                Log(session, "Data changed.");
                OnDataReceived?.Invoke(session.SessionId, new ModbusDataSnapshot
                {
                    DataType = parameters.DataType,
                    StartAddress = parameters.StartAddress,
                    RegisterSizePerItem = 1,
                    Bits = bits
                });
            }
        }

        private async Task ReadAndPublishRegistersAsync(ModbusSessionState session, PollingParameters parameters)
        {
            var connection = session.Connection;
            if (connection == null || session.RegisterReadBuffer == null) return;

            ushort[] destination = session.RegisterReadBuffer;
            int registerSizePerItem = GetRegisterSizeForDataType(parameters.DataType);

            int? probeTimeout = session.TimeoutBackoffUntilUtc.HasValue ? ProbeTimeoutMs : (int?)null;

            await connection.TransactionLock.WaitAsync();
            try
            {
                if (parameters.FunctionCode == ModbusFunctionCode.ReadHoldingRegisters)
                    await connection.Client.ReadHoldingRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination, probeTimeout);
                else
                    await connection.Client.ReadInputRegistersAsync(parameters.SlaveId, parameters.StartAddress, destination, probeTimeout);
            }
            finally
            {
                connection.TransactionLock.Release();
            }

            if (HasValuesChanged(session.OldValues, destination))
            {
                session.OldValues = (ushort[])destination.Clone();
                Log(session, "Data changed.");
                OnDataReceived?.Invoke(session.SessionId, new ModbusDataSnapshot
                {
                    DataType = parameters.DataType,
                    StartAddress = parameters.StartAddress,
                    RegisterSizePerItem = registerSizePerItem,
                    Registers = destination
                });
            }
        }

        private static bool HasValuesChanged(ushort[]? oldValues, ushort[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;
            for (int i = 0; i < oldValues.Length; i++) if (oldValues[i] != newValues[i]) return true;
            return false;
        }

        private static bool HasBitValuesChanged(bool[]? oldValues, bool[] newValues)
        {
            if (oldValues == null || oldValues.Length != newValues.Length) return true;
            for (int i = 0; i < oldValues.Length; i++) if (oldValues[i] != newValues[i]) return true;
            return false;
        }

        private static int GetRegisterSizeForDataType(string dataType)
        {
            return dataType switch
            {
                "Float (32-bit)" or "Float Inverse (32-bit)" or
                "Long (32-bit)" or "Long Inverse (32-bit)" => 2,
                "Double (64-bit)" or "Double Inverse (64-bit)" => 4,
                _ => 1
            };
        }

        private void SetPhase(ModbusSessionState session, ConnectionPhase phase, string message)
        {
            session.CurrentPhase = phase;
            if (OnPhaseChanged != null) OnPhaseChanged(session.SessionId, phase, message);
        }

        private void Log(ModbusSessionState session, string message)
        {
            if (OnLog != null) OnLog(session.SessionId, message);
        }

        /// <summary>
        /// Subscribes this session's own forwarding handler to the just-acquired connection's
        /// client. Note the connection may be shared with other sessions targeting the same
        /// IP:Port (pooled by ModbusConnectionManager) — each sharer attaches its own handler, so
        /// traffic on a shared socket is reported to every session attached to it, tagged with
        /// each one's own SessionId. That mirrors the fact that they are, physically, watching
        /// the same wire.
        /// </summary>
        private void AttachTraffic(ModbusSessionState session)
        {
            if (session.Connection == null) return;

            Action<byte[], bool> handler = (data, isTx) => OnTraffic?.Invoke(session.SessionId, data, isTx);
            session.TrafficHandler = handler;
            session.Connection.Client.OnTraffic += handler;
        }

        private void DetachTraffic(ModbusSessionState session)
        {
            if (session.Connection != null && session.TrafficHandler != null)
            {
                session.Connection.Client.OnTraffic -= session.TrafficHandler;
            }
            session.TrafficHandler = null;
        }

        // ---------------------------------------------------------
        // WRITE COMMANDS (top-down flow from REST controllers)
        // ---------------------------------------------------------

        public Task WriteSingleCoilAsync(string sessionId, byte slaveId, ushort address, bool value)
            => WithTransactionLockAsync(sessionId, connection => connection.Client.WriteSingleCoilAsync(slaveId, address, value));

        public Task WriteSingleRegisterAsync(string sessionId, byte slaveId, ushort address, ushort value)
            => WithTransactionLockAsync(sessionId, connection => connection.Client.WriteSingleRegisterAsync(slaveId, address, value));

        public Task WriteMultipleRegistersAsync(string sessionId, byte slaveId, ushort startAddress, ushort[] values)
            => WithTransactionLockAsync(sessionId, connection => connection.Client.WriteMultipleRegistersAsync(slaveId, startAddress, values));

        private async Task WithTransactionLockAsync(string sessionId, Func<PooledConnection, Task> action)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new InvalidOperationException($"Session '{sessionId}' does not exist.");

            var connection = session.Connection;
            if (connection == null)
                throw new InvalidOperationException($"Session '{sessionId}' is not connected.");

            await connection.TransactionLock.WaitAsync();
            try
            {
                await action(connection);
            }
            finally
            {
                connection.TransactionLock.Release();
            }
        }
    }
}
