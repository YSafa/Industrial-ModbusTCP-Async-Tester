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
    // DİNAMİK KONFİGÜRASYON ALTYAPISI
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
        Log("Ctrl+C algılandı, bağlantı ve döngü temiz şekilde sonlandırılıyor...", ConsoleColor.Yellow);
        cts.Cancel();
    };

    Log("Modbus TCP Console Driver başlatılıyor (appsettings.json ile dinamik konfigürasyon)...", ConsoleColor.Cyan);

    var client = new ModbusClient(currentIp, currentPort)
    {
        ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
        IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
    };

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
        // DIŞ DÖNGÜ (OUTER LOOP): Bağlantıyı sağlamak/yeniden sağlamak.
        // ===========================================================
        while (!cts.Token.IsCancellationRequested)
        {
            bool connected = await EstablishConnectionAsync();
            if (!connected)
            {
                break; // Yalnızca Ctrl+C nedeniyle buraya düşülür.
            }

            oldValues = null;
            lastLoggedError = null;

            // ===========================================================
            // İÇ DÖNGÜ (INNER LOOP): PeriodicTimer tabanlı asıl polling akışı.
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
                        Log($"Polling aralığı değişti: {lastKnownIntervalMs}ms -> {liveIntervalMs}ms. Timer yeniden başlatılıyor.", ConsoleColor.Cyan);
                        timer.Dispose();
                        timer = new PeriodicTimer(TimeSpan.FromMilliseconds(liveIntervalMs));
                        lastKnownIntervalMs = liveIntervalMs;
                    }

                    if (liveIp != currentIp || livePort != currentPort)
                    {
                        Log($"Bağlantı parametreleri değişti: '{currentIp}:{currentPort}' -> '{liveIp}:{livePort}'. Yeniden bağlanılıyor.", ConsoleColor.Cyan);

                        var temporaryClient = new ModbusClient(liveIp, livePort)
                        {
                            ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
                            IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
                        };

                        try
                        {
                            await temporaryClient.ConnectAsync();

                            client.Disconnect();
                            client = temporaryClient;
                            currentIp = liveIp;
                            currentPort = livePort;

                            Log($"'{currentIp}:{currentPort}' adresine yeniden bağlantı başarılı.", ConsoleColor.Green);
                            oldValues = null;
                        }
                        catch (Exception ex)
                        {
                            temporaryClient.Disconnect();
                            Log($"Yeni parametrelerle bağlantı kurulamadı: {ex.Message} — bir sonraki tick'te tekrar denenecek.", ConsoleColor.Red);
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

                        string errorMessage = $"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }
                    }
                    catch (ModbusTimeoutException ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"ZAMAN AŞIMI: {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }

                        oldValues = null;

                        bool reconnected = await TryReconnectAsync();
                        if (!reconnected)
                        {
                            Log("Yeniden bağlanma denemeleri tükendi, bağlantı fazına geri dönülüyor...", ConsoleColor.Red);
                            break;
                        }
                    }
                    catch (ModbusConnectionException ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"BAĞLANTI HATASI: {ex.Message}";

                        if (lastLoggedError != errorMessage)
                        {
                            Log(errorMessage, ConsoleColor.Red);
                            lastLoggedError = errorMessage;
                        }

                        oldValues = null;

                        bool reconnected = await TryReconnectAsync();
                        if (!reconnected)
                        {
                            Log("Yeniden bağlanma denemeleri tükendi, bağlantı fazına geri dönülüyor...", ConsoleColor.Red);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (cts.Token.IsCancellationRequested) break;

                        string errorMessage = $"BEKLENMEYEN HATA: {ex.Message}";

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
                timer.Dispose();
            }
        }
    }
    finally
    {
        client.Disconnect();
        Log("Bağlantı kapatıldı, uygulama sonlandırıldı.", ConsoleColor.Cyan);
    }

    // ---------------------------------------------------------
    // YARDIMCI YEREL METOTLAR
    // ---------------------------------------------------------

    /// <summary>
    /// Cihaza bağlantı kurulana kadar (veya Ctrl+C gelene kadar) 5 saniye aralıklarla dener.
    /// Her denemeden ÖNCE IP/Port bilgisini appsettings.json'dan CANLI okur; böylece sürücü
    /// henüz hiç bağlanamamışken bile kullanıcı config'i düzeltirse bir sonraki denemede
    /// otomatik olarak taze adrese yönelir (tavuk-yumurta kilidi çözülür).
    /// </summary>
    async Task<bool> EstablishConnectionAsync()
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                string liveIp   = settings.GetValue<string>("TargetIp")   ?? currentIp;
                int    livePort = settings.GetValue<int?>("TargetPort")   ?? currentPort;

                if (liveIp != currentIp || livePort != currentPort)
                {
                    Log($"Bağlantı beklerken parametreler değişti: '{currentIp}:{currentPort}' -> '{liveIp}:{livePort}'. Yeni hedef deneniyor.", ConsoleColor.Cyan);
                    client.Disconnect();

                    currentIp = liveIp;
                    currentPort = livePort;

                    client = new ModbusClient(currentIp, currentPort)
                    {
                        ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
                        IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
                    };
                }

                await client.ConnectAsync();
                Log($"'{currentIp}:{currentPort}' adresine bağlantı başarılı.", ConsoleColor.Green);
                return true;
            }
            catch (ModbusConnectionException ex)
            {
                Log($"BAĞLANTI HATASI: {ex.Message} — 5 saniye sonra tekrar denenecek.", ConsoleColor.Red);
            }
            catch (ModbusTimeoutException ex)
            {
                Log($"BAĞLANTI ZAMAN AŞIMI: {ex.Message} — 5 saniye sonra tekrar denenecek.", ConsoleColor.Red);
            }
            catch (Exception ex)
            {
                Log($"BEKLENMEYEN BAĞLANTI HATASI: {ex.Message} — 5 saniye sonra tekrar denenecek.", ConsoleColor.Red);
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

            Log($"Yeniden bağlanılıyor... Deneme {attempt}/{maxAttempts}", ConsoleColor.Yellow);

            try
            {
                await Task.Delay(delayMs, cts.Token);
                await client.ConnectAsync();

                Log("Yeniden bağlantı başarılı, polling devam ediyor.", ConsoleColor.Green);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log($"Deneme {attempt} başarısız: {ex.Message}", ConsoleColor.DarkYellow);
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