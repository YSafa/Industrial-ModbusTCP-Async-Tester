using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;

// ---------------------------------------------------------
// DİNAMİK KONFİGÜRASYON ALTYAPISI
// ---------------------------------------------------------
IConfigurationRoot config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// "ModbusSettings" bölümüne referans; reloadOnChange sayesinde dosya değiştiğinde
// bu section nesnesi arkada otomatik güncellenir, tekrar Build() çağırmaya gerek yoktur.
IConfigurationSection settings = config.GetSection("ModbusSettings");

// Bağlantı parametrelerini (IP/Port) yalnızca ModbusClient oluşturulurken bir kez okuyoruz;
// bunlar runtime'da değişirse ayrı bir mantıkla client yeniden kurulacak (aşağıda ele alınıyor).
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
// ANA POLLING DÖNGÜSÜ: PeriodicTimer + her turda config'den canlı okuma.
// PeriodicTimer'ın aralığı runtime'da değiştirilemediği için, interval değişikliği
// algılandığında timer Dispose edilip yeni aralıkla yeniden oluşturuluyor.
// ---------------------------------------------------------
long cycleCounter = 0;
ushort[]? oldValues = null;

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

        // --- Her döngü adımında en güncel ayarları config'den okuyoruz. ---
        byte   liveSlaveId      = (byte)(settings.GetValue<int?>("SlaveId") ?? 1);
        ushort liveStartAddress = (ushort)(settings.GetValue<int?>("StartAddress") ?? 0);
        ushort liveQuantity     = (ushort)(settings.GetValue<int?>("Quantity") ?? 1);
        int    liveIntervalMs   = settings.GetValue<int?>("PollingIntervalMs") ?? 500;
        string liveIp           = settings.GetValue<string>("TargetIp") ?? currentIp;
        int    livePort         = settings.GetValue<int?>("TargetPort") ?? currentPort;

        // --- Polling aralığı değiştiyse timer'ı yeni süreyle yeniden oluştur. ---
        if (liveIntervalMs != lastKnownIntervalMs && liveIntervalMs > 0)
        {
            Log($"Polling aralığı değişti: {lastKnownIntervalMs}ms -> {liveIntervalMs}ms. Timer yeniden başlatılıyor.", ConsoleColor.Cyan);
            timer.Dispose();
            timer = new PeriodicTimer(TimeSpan.FromMilliseconds(liveIntervalMs));
            lastKnownIntervalMs = liveIntervalMs;
        }

        // --- IP/Port değiştiyse mevcut bağlantıyı kapatıp ModbusClient'ı yeniden kur. ---
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
                oldValues = null; // Yeni cihaz/bağlantı; önceki veri karşılaştırması artık geçersiz.
            }
            catch (Exception ex)
            {
                Log($"Yeni parametrelerle bağlantı kurulamadı: {ex.Message}", ConsoleColor.Red);
                continue; // Bu turu atla, bir sonraki tick'te tekrar denenecek.
            }
        }

        try
        {
            ushort[] registers = await client.ReadHoldingRegistersAsync(liveSlaveId, liveStartAddress, liveQuantity);

            // Span tabanlı, allocation'sız karşılaştırma; sadece veri değiştiğinde loglama tetiklenir.
            bool hasChanged = oldValues == null ||
                              !MemoryExtensions.SequenceEqual<ushort>(oldValues.AsSpan(), registers.AsSpan());

            if (hasChanged)
            {
                oldValues = registers;

                string values = string.Join(", ", registers);
                Log($"[DATA CHANGED - Cycle #{cycleCounter}] -> [{values}]", ConsoleColor.Green);
            }
            else if (cycleCounter % 100 == 0)
            {
                Log($"[HEARTBEAT - Cycle #{cycleCounter}] Driver alive, data stream stable.", ConsoleColor.DarkGray);
            }
        }
        catch (ModbusProtocolException ex)
        {
            Log($"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}", ConsoleColor.Red);
        }
        catch (ModbusTimeoutException ex)
        {
            Log($"ZAMAN AŞIMI: {ex.Message}", ConsoleColor.Red);
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
            Log($"BAĞLANTI HATASI: {ex.Message}", ConsoleColor.Red);
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