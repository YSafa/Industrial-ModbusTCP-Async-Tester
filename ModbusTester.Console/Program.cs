using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ModbusTester.Core;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

    // ---------------------------------------------------------
    // DYNAMIC CONFIGURATION INFRASTRUCTURE
    // ---------------------------------------------------------
    IConfigurationRoot config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .Build();

    IConfigurationSection settings = config.GetSection("ModbusSettings");

    string currentIp   = settings.GetValue<string>("TargetIp")   ?? "127.0.0.1";
    int    currentPort = settings.GetValue<int?>("TargetPort")   ?? 502;

    // ---------------------------------------------------------
    // GRACEFUL SHUTDOWN
    // ---------------------------------------------------------
    using var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        Log("Ctrl+C detected, closing connection and shutting down cleanly...", ConsoleColor.Yellow);
        cts.Cancel();
    };

    Log("Modbus TCP Console Driver starting (dynamic configuration via appsettings.json)...", ConsoleColor.Cyan);

    var client = new ModbusClient(currentIp, currentPort)
    {
        ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
        IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
    };

    // State that must PERSIST across outer/inner loop transitions; defined once here so that
    // counters like cycleCounter/heartbeat don't reset when the connection drops and recovers.
    long cycleCounter = 0;
    ushort[]? oldValues = null;
    ushort oldStartAddress = 0;
    string lastKnownDataType = settings.GetValue<string>("DataType") ?? "Unsigned (16-bit)";
    int lastKnownRegisterSize = GetRegisterSizeForDataType(lastKnownDataType);
    string? lastLoggedError = null;
    int lastKnownIntervalMs = settings.GetValue<int?>("PollingIntervalMs") ?? 500;

    int initialQuantity = settings.GetValue<int?>("Quantity") ?? 1;
    ushort[] readBuffer = new ushort[Math.Max(1, initialQuantity) * lastKnownRegisterSize];

    try
    {
        // ===========================================================
        // OUTER LOOP: its only job is to establish/re-establish the connection.
        // Runs for the entire lifetime of the application; only broken by Ctrl+C (cts.Cancel).
        // ===========================================================
        while (!cts.Token.IsCancellationRequested)
        {
            bool connected = await EstablishConnectionAsync();
            if (!connected)
            {
                // Only reached due to a cancellation request (Ctrl+C).
                break;
            }

            // Any previous data cache is invalid on a new/re-established connection; the next
            // read will automatically be treated as a "layout changed" event and log in full.
            oldValues = null;
            lastLoggedError = null;

            // ===========================================================
            // INNER LOOP: the actual PeriodicTimer-driven polling flow.
            // If the connection drops and TryReconnectAsync is exhausted, ONLY this loop breaks;
            // the application does NOT exit — control returns to the outer loop (connection phase).
            // ===========================================================
            PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMilliseconds(lastKnownIntervalMs));

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    bool tickReceived;
                    try
                    {
                        tickReceived = await timer.WaitForNextTickAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (!tickReceived) break;

                    cycleCounter++;

                    byte   liveSlaveId      = (byte)(settings.GetValue<int?>("SlaveId") ?? 1);
                    ushort liveStartAddress = (ushort)(settings.GetValue<int?>("StartAddress") ?? 0);
                    int    liveQuantity     = settings.GetValue<int?>("Quantity") ?? 1;
                    int    liveIntervalMs   = settings.GetValue<int?>("PollingIntervalMs") ?? 500;
                    string liveIp           = settings.GetValue<string>("TargetIp") ?? currentIp;
                    int    livePort         = settings.GetValue<int?>("TargetPort") ?? currentPort;
                    string liveDataType     = settings.GetValue<string>("DataType") ?? "Unsigned (16-bit)";

                    if (liveIntervalMs != lastKnownIntervalMs && liveIntervalMs > 0)
                    {
                        Log($"Polling interval changed: {lastKnownIntervalMs}ms -> {liveIntervalMs}ms. Restarting timer.", ConsoleColor.Cyan);
                        timer.Dispose();
                        timer = new PeriodicTimer(TimeSpan.FromMilliseconds(liveIntervalMs));
                        lastKnownIntervalMs = liveIntervalMs;
                    }

                    if (liveIp != currentIp || livePort != currentPort)
                    {
                        Log($"Connection parameters changed: '{currentIp}:{currentPort}' -> '{liveIp}:{livePort}'. Reconnecting.", ConsoleColor.Cyan);

                        // CRITICAL: currentIp/currentPort are NOT mutated yet. We first attempt to
                        // connect with the new parameters via a temporary client; no persistent state
                        // is updated until success is PROVEN. Otherwise, if an invalid/unreachable IP
                        // is written to the config, currentIp would be updated prematurely, the next
                        // tick would see "no change", this block would never fire again, and the
                        // service would keep hammering a dead socket until TryReconnectAsync's 5
                        // attempts are exhausted and the loop kills itself entirely.
                        var temporaryClient = new ModbusClient(liveIp, livePort)
                        {
                            ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
                            IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
                        };

                        try
                        {
                            await temporaryClient.ConnectAsync();

                            // Connection is PROVEN successful; only now do we swap the client and
                            // update the persistent state.
                            client.Disconnect();
                            client = temporaryClient;
                            currentIp = liveIp;
                            currentPort = livePort;

                            Log($"Reconnected successfully to '{currentIp}:{currentPort}'.", ConsoleColor.Green);
                            oldValues = null;
                        }
                        catch (Exception ex)
                        {
                            // Failed: currentIp/currentPort are left UNTOUCHED. On the next tick,
                            // liveIp/livePort will still differ from currentIp/currentPort, so this
                            // block will automatically retry — the service never gives up.
                            temporaryClient.Disconnect();
                            Log($"Could not connect with new parameters: {ex.Message} — will retry on the next tick.", ConsoleColor.Red);
                            continue;
                        }
                    }

                    try
                    {
                        int liveRegisterSize = GetRegisterSizeForDataType(liveDataType);

                        int requiredLength = Math.Max(1, liveQuantity) * liveRegisterSize;
                        if (readBuffer.Length != requiredLength)
                        {
                            readBuffer = new ushort[requiredLength];
                            oldValues = null;
                        }

                        await client.ReadHoldingRegistersAsync(liveSlaveId, liveStartAddress, readBuffer);

                        if (lastLoggedError != null)
                        {
                            Log("[RECOVERY] Driver successfully recovered from previous errors. Data stream is back to normal.", ConsoleColor.Cyan);
                            lastLoggedError = null;
                        }

                        bool layoutChanged = oldValues == null ||
                                             oldValues.Length != readBuffer.Length ||
                                             oldStartAddress != liveStartAddress ||
                                             lastKnownDataType != liveDataType;

                        string? changeLog = layoutChanged
                            ? BuildFullChangeLog(readBuffer, liveStartAddress, liveDataType, liveRegisterSize)
                            : BuildDiffChangeLog(oldValues!, readBuffer, liveStartAddress, liveDataType, liveRegisterSize);

                        if (changeLog != null)
                        {
                            Log($"[DATA CHANGED - Cycle #{cycleCounter}] -> {changeLog}", ConsoleColor.Green);

                            // Memory guard: only clone when data actually changed (or on first
                            // connection/layout change). If the data is stable (changeLog == null),
                            // oldValues already matches readBuffer's current content, so cloning
                            // would be a pointless per-tick heap allocation.
                            oldValues = (ushort[])readBuffer.Clone();
                            oldStartAddress = liveStartAddress;
                            lastKnownDataType = liveDataType;
                            lastKnownRegisterSize = liveRegisterSize;
                        }
                        else if (cycleCounter % 100 == 0)
                        {
                            Log($"[HEARTBEAT - Cycle #{cycleCounter}] Driver alive, data stream stable.", ConsoleColor.DarkGray);
                        }
                    }
                    catch (ModbusProtocolException ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"PROTOCOL ERROR (Code: {ex.ExceptionCode}): {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }
                    }
                    catch (ModbusTimeoutException ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"TIMEOUT: {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }

                        oldValues = null;

                        bool reconnected = await TryReconnectAsync();
                        if (!reconnected)
                        {
                            // ONLY the inner loop breaks; the application does NOT exit. The outer
                            // loop will return to EstablishConnectionAsync on its next iteration.
                            Log("Reconnect attempts exhausted, returning to connection phase...", ConsoleColor.Red);
                            break;
                        }
                    }
                    catch (ModbusConnectionException ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"CONNECTION ERROR: {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }

                        oldValues = null;

                        bool reconnected = await TryReconnectAsync();
                        if (!reconnected)
                        {
                            Log("Reconnect attempts exhausted, returning to connection phase...", ConsoleColor.Red);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"UNEXPECTED ERROR: {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }
                    }
                }
            }
            finally
            {
                // Timer is disposed on every inner loop exit (Ctrl+C or reconnect exhaustion alike);
                // the outer loop will build a fresh one on its next iteration.
                timer.Dispose();
            }

            // If the inner loop exited via Ctrl+C, the outer loop's condition will already be
            // false and the flow will end naturally. If it exited due to reconnect exhaustion,
            // the outer loop will re-run EstablishConnectionAsync and patiently wait again.
        }
    }
    finally
    {
        client.Disconnect();
        Log("Connection closed, application terminated.", ConsoleColor.Cyan);
    }

    // ---------------------------------------------------------
    // LOCAL HELPER FUNCTIONS (intentionally non-static so they can capture outer-scope
    // configuration/state variables without triggering compiler error CS8421)
    // ---------------------------------------------------------

    /// <summary>
    /// Retries connecting to the device every 5 seconds (or until Ctrl+C) — used both for the
    /// application's initial startup and whenever the outer loop is called back into after a
    /// connection drop. Live IP/Port are re-read from config on every attempt, before every
    /// connect call, so that a config fix takes effect on the very next retry even if the
    /// application has never successfully connected yet (chicken-and-egg fix). Returns true once
    /// connected; returns false only if cancellation was requested.
    /// </summary>
    async Task<bool> EstablishConnectionAsync()
    {
        while (!cts.Token.IsCancellationRequested)
        {
            string liveIp   = settings.GetValue<string>("TargetIp")   ?? currentIp;
            int    livePort = settings.GetValue<int?>("TargetPort")   ?? currentPort;

            if (liveIp != currentIp || livePort != currentPort)
            {
                Log($"Parameters changed while waiting for connection: '{currentIp}:{currentPort}' -> '{liveIp}:{livePort}'. Trying new target.", ConsoleColor.Cyan);
                client.Disconnect();

                currentIp = liveIp;
                currentPort = livePort;

                client = new ModbusClient(currentIp, currentPort)
                {
                    ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
                    IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
                };
            }

            try
            {
                await client.ConnectAsync();
                Log($"Connected successfully to '{currentIp}:{currentPort}'.", ConsoleColor.Green);
                return true;
            }
            catch (ModbusConnectionException ex)
            {
                Log($"CONNECTION ERROR: {ex.Message} — retrying in 5 seconds.", ConsoleColor.Red);
            }
            catch (ModbusTimeoutException ex)
            {
                Log($"CONNECTION TIMEOUT: {ex.Message} — retrying in 5 seconds.", ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                // Also catches raw SocketException/IOException, e.g. if the OS network stack
                // isn't ready yet at boot; the service must never crash on this.
                Log($"UNEXPECTED CONNECTION ERROR: {ex.Message} — retrying in 5 seconds.", ConsoleColor.Red);
            }

            try
            {
                await Task.Delay(5000, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    int GetRegisterSizeForDataType(string dataType)
    {
        return dataType switch
        {
            "Float (32-bit)" or "Float Inverse (32-bit)" or
            "Long (32-bit)"  or "Long Inverse (32-bit)"   => 2,
            "Double (64-bit)" or "Double Inverse (64-bit)" => 4,
            _ => 1
        };
    }

    string GetRegisterDisplayValue(ushort[] registers, string dataType, int i, int registerSizePerItem)
    {
        switch (dataType)
        {
            case "Unsigned (16-bit)": return registers[i].ToString();
            case "Signed (16-bit)":   return ModbusDataConverter.ToSigned(registers[i]).ToString();
            case "Hex":                return ModbusDataConverter.ToHex(registers[i]);
            case "Binary":             return ModbusDataConverter.ToBinary(registers[i]);

            case "Float (32-bit)":
                return ModbusDataConverter.ToFloat(registers.AsSpan(i, registerSizePerItem), inverse: true).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "Float Inverse (32-bit)":
                return ModbusDataConverter.ToFloat(registers.AsSpan(i, registerSizePerItem), inverse: false).ToString(System.Globalization.CultureInfo.InvariantCulture);

            case "Long (32-bit)":
                return ModbusDataConverter.ToLong(registers.AsSpan(i, registerSizePerItem), inverse: true).ToString();
            case "Long Inverse (32-bit)":
                return ModbusDataConverter.ToLong(registers.AsSpan(i, registerSizePerItem), inverse: false).ToString();

            case "Double (64-bit)":
                return ModbusDataConverter.ToDouble(registers.AsSpan(i, registerSizePerItem), inverse: true).ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "Double Inverse (64-bit)":
                return ModbusDataConverter.ToDouble(registers.AsSpan(i, registerSizePerItem), inverse: false).ToString(System.Globalization.CultureInfo.InvariantCulture);

            default: return registers[i].ToString();
        }
    }

    string BuildFullChangeLog(ushort[] registers, ushort startAddress, string dataType, int registerSizePerItem)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < registers.Length; i += registerSizePerItem)
        {
            ushort itemAddress = (ushort)(startAddress + i);
            string value = GetRegisterDisplayValue(registers, dataType, i, registerSizePerItem);
            sb.Append('[').Append(itemAddress).Append(": ").Append(value).Append("] ");
        }

        return sb.ToString().TrimEnd();
    }

    string? BuildDiffChangeLog(ushort[] oldRegisters, ushort[] newRegisters, ushort startAddress, string dataType, int registerSizePerItem)
    {
        var sb = new StringBuilder();
        bool anyChanged = false;

        for (int i = 0; i < newRegisters.Length; i += registerSizePerItem)
        {
            // Compares raw registers block-by-block via Span, allocation-free.
            ReadOnlySpan<ushort> oldSlice = oldRegisters.AsSpan(i, registerSizePerItem);
            ReadOnlySpan<ushort> newSlice = newRegisters.AsSpan(i, registerSizePerItem);

            if (!oldSlice.SequenceEqual(newSlice))
            {
                anyChanged = true;
                ushort itemAddress = (ushort)(startAddress + i);
                string oldValue = GetRegisterDisplayValue(oldRegisters, dataType, i, registerSizePerItem);
                string newValue = GetRegisterDisplayValue(newRegisters, dataType, i, registerSizePerItem);
                sb.Append('[').Append(itemAddress).Append(": ").Append(oldValue).Append(" -> ").Append(newValue).Append("] ");
            }
        }

        return anyChanged ? sb.ToString().TrimEnd() : null;
    }

    async Task<bool> TryReconnectAsync()
    {
        int maxAttempts = settings.GetValue<int?>("ReconnectMaxAttempts") ?? 5;
        int delayMs      = settings.GetValue<int?>("ReconnectDelayMs")     ?? 2000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (cts.Token.IsCancellationRequested) return false;

            Log($"Reconnecting... Attempt {attempt}/{maxAttempts}", ConsoleColor.Yellow);

            try
            {
                await Task.Delay(delayMs, cts.Token);
                await client.ConnectAsync();

                Log("Reconnected successfully, polling resumed.", ConsoleColor.Green);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log($"Attempt {attempt} failed: {ex.Message}", ConsoleColor.DarkYellow);
            }
        }

        return false;
    }

    void Log(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        Console.ResetColor();
    }