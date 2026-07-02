using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ModbusTester.Core;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;

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
    // GRACEFUL SHUTDOWN: Ctrl+C sinyalini yakalayıp CancellationToken'a bağlıyoruz.
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

    try
    {
        await client.ConnectAsync();
        Log($"'{currentIp}:{currentPort}' adresine bağlantı başarılı.", ConsoleColor.Green);
    }
    catch (ModbusConnectionException ex)
    {
        Log($"BAĞLANTI HATASI: {ex.Message}", ConsoleColor.Red);
        return;
    }
    catch (ModbusTimeoutException ex)
    {
        Log($"BAĞLANTI ZAMAN AŞIMI: {ex.Message}", ConsoleColor.Red);
        return;
    }

    // ---------------------------------------------------------
    // ANA POLLING DÖNGÜSÜ
    // ---------------------------------------------------------
    long cycleCounter = 0;

    ushort[]? oldValues = null;
    ushort oldStartAddress = 0;

    // Hata tekrarını (log spam'ini) önlemek için son loglanan hata mesajını saklıyoruz.
    // null ise "hata durumunda değiliz" anlamına gelir.
    string? lastLoggedError = null;

    int lastKnownIntervalMs = settings.GetValue<int?>("PollingIntervalMs") ?? 500;
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
            ushort liveQuantity     = (ushort)(settings.GetValue<int?>("Quantity") ?? 1);
            int    liveIntervalMs   = settings.GetValue<int?>("PollingIntervalMs") ?? 500;
            string liveIp           = settings.GetValue<string>("TargetIp") ?? currentIp;
            int    livePort         = settings.GetValue<int?>("TargetPort") ?? currentPort;

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

                client.Disconnect();
                currentIp   = liveIp;
                currentPort = livePort;

                client = new ModbusClient(currentIp, currentPort)
                {
                    ConnectTimeoutMs = settings.GetValue<int?>("ConnectTimeoutMs") ?? 3000,
                    IoTimeoutMs       = settings.GetValue<int?>("IoTimeoutMs")     ?? 3000
                };

                try
                {
                    await client.ConnectAsync();
                    Log($"'{currentIp}:{currentPort}' adresine yeniden bağlantı başarılı.", ConsoleColor.Green);
                    oldValues = null;
                }
                catch (Exception ex)
                {
                    Log($"Yeni parametrelerle bağlantı kurulamadı: {ex.Message}", ConsoleColor.Red);
                    continue;
                }
            }

            try
            {
                ushort[] registers = await client.ReadHoldingRegistersAsync(liveSlaveId, liveStartAddress, liveQuantity);

                // Başarılı okuma gerçekleşti; eğer sistem daha önce bir hata durumundaysa, bunu
                // belirgin bir "RECOVERY" mesajıyla bildiriyoruz ve hata hafızasını sıfırlıyoruz.
                if (lastLoggedError != null)
                {
                    Log("[RECOVERY] Driver successfully recovered from previous errors. Data stream is back to normal.", ConsoleColor.Cyan);
                    lastLoggedError = null;
                }

                bool layoutChanged = oldValues == null ||
                                     oldValues.Length != registers.Length ||
                                     oldStartAddress != liveStartAddress;

                string? changeLog = layoutChanged
                    ? BuildFullChangeLog(registers, liveStartAddress)
                    : BuildDiffChangeLog(oldValues!, registers, liveStartAddress);

                if (changeLog != null)
                {
                    Log($"[DATA CHANGED - Cycle #{cycleCounter}] -> {changeLog}", ConsoleColor.Green);
                }
                else if (cycleCounter % 100 == 0)
                {
                    Log($"[HEARTBEAT - Cycle #{cycleCounter}] Driver alive, data stream stable.", ConsoleColor.DarkGray);
                }

                oldValues = registers;
                oldStartAddress = liveStartAddress;
            }
            catch (ModbusProtocolException ex)
            {
                // Kapanış sinyali zaten verildiyse, bu tick'teki protokol hatasını loglamaya
                // veya işlemeye devam etmenin bir anlamı yok; döngü anında ve temizce kırılır.
                if (cts.Token.IsCancellationRequested) break;
                
                string errorMessage = $"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}";

                // Aynı hata art arda tekrar ediyorsa sessizce geçiyoruz; yalnızca ilk görüldüğünde
                // veya bir öncekinden farklıysa loglanıyor.
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
                    Log("Yeniden bağlanma denemeleri tükendi, uygulama kapatılıyor.", ConsoleColor.Red);
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
                    Log("Yeniden bağlanma denemeleri tükendi, uygulama kapatılıyor.", ConsoleColor.Red);
                    break;
                }
            }
        }
    }
    finally
    {
        timer.Dispose();
        client.Disconnect();
        Log("Bağlantı kapatıldı, uygulama sonlandırıldı.", ConsoleColor.Cyan);
    }

    // ---------------------------------------------------------
    // YARDIMCI YEREL METOTLAR (Non-static: dış kapsamdaki değişkenleri/config'i
    // closure ile yakalayabilmek için CS8421 hatasından kaçınmak amacıyla static değildir)
    // ---------------------------------------------------------

    string BuildFullChangeLog(ushort[] registers, ushort startAddress)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < registers.Length; i++)
        {
            ushort actualAddress = (ushort)(startAddress + i);
            sb.Append($"[{actualAddress}: {registers[i]}] ");
        }

        return sb.ToString().TrimEnd();
    }

    string? BuildDiffChangeLog(ushort[] oldRegisters, ushort[] newRegisters, ushort startAddress)
    {
        var sb = new StringBuilder();
        bool anyChanged = false;

        for (int i = 0; i < newRegisters.Length; i++)
        {
            if (oldRegisters[i] != newRegisters[i])
            {
                anyChanged = true;
                ushort actualAddress = (ushort)(startAddress + i);
                sb.Append($"[{actualAddress}: {oldRegisters[i]} -> {newRegisters[i]}] ");
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