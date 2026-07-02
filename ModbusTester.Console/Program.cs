using System;
using System.Threading;
using System.Threading.Tasks;
using ModbusTester.Core.Core;
using ModbusTester.Core.Exceptions;

// ---------------------------------------------------------
// SABİT PARAMETRELER (Yerel test için hardcoded)
// ---------------------------------------------------------
const string TargetIp        = "127.0.0.1";
const int    TargetPort      = 502;
const byte   SlaveId         = 1;
const ushort StartAddress    = 0;
const ushort Quantity        = 135;
const int    PollingIntervalMs = 500;
const int    ReconnectMaxAttempts = 5;
const int    ReconnectDelayMs     = 2000;
const int    HeartbeatCycleInterval = 100;

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

Log("Modbus TCP Console Driver başlatılıyor...", ConsoleColor.Cyan);

var client = new ModbusClient(TargetIp, TargetPort)
{
    ConnectTimeoutMs = 3000,
    IoTimeoutMs = 3000
};

try
{
    await client.ConnectAsync();
    Log($"'{TargetIp}:{TargetPort}' adresine bağlantı başarılı.", ConsoleColor.Green);
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
// ANA POLLING DÖNGÜSÜ: PeriodicTimer ile modern, tick-drift'siz zamanlama.
// ---------------------------------------------------------
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PollingIntervalMs));
long cycleCounter = 0;

// Değişim takibi için önceki okumayı saklayan referans; null-safe olarak tanımlandı.
ushort[]? oldValues = null;

try
{
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        cycleCounter++;

        try
        {
            ushort[] registers = await client.ReadHoldingRegistersAsync(SlaveId, StartAddress, Quantity);

            // Span tabanlı karşılaştırma: byte-level SIMD hızlandırmalı, allocation'sız ve çok hızlı.
            // oldValues null ise (ilk okuma) veya dizi içerik olarak değiştiyse "değişti" sayılır.
            bool hasChanged = oldValues == null ||
                              !MemoryExtensions.SequenceEqual<ushort>(oldValues.AsSpan(), registers.AsSpan());

            if (hasChanged)
            {
                oldValues = registers;

                string values = string.Join(", ", registers);
                Log($"[DATA CHANGED - Cycle #{cycleCounter}] -> [{values}]", ConsoleColor.Green);
            }
            else if (cycleCounter % HeartbeatCycleInterval == 0)
            {
                // Veri değişmese bile driver'ın donmadığını kanıtlamak için periyodik nabız sinyali.
                Log($"[HEARTBEAT - Cycle #{cycleCounter}] Driver alive, data stream stable.", ConsoleColor.DarkGray);
            }
            // Veri değişmedi ve heartbeat turu değilse: tamamen sessiz kal (CPU/IO tasarrufu).
        }
        catch (ModbusProtocolException ex)
        {
            Log($"PROTOKOL HATASI (Kod: {ex.ExceptionCode}): {ex.Message}", ConsoleColor.Red);
        }
        catch (ModbusTimeoutException ex)
        {
            Log($"ZAMAN AŞIMI: {ex.Message}", ConsoleColor.Red);

            // Bağlantı koptuğu için önceki veri hafızası artık geçersiz; sıfırlıyoruz ki
            // yeniden bağlanınca ilk gelen veri doğru şekilde "değişti" sayılıp loglansın.
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
catch (OperationCanceledException)
{
    // Ctrl+C ile normal şekilde iptal edildi.
}
finally
{
    client.Disconnect();
    Log("Bağlantı kapatıldı, uygulama sonlandırıldı.", ConsoleColor.Cyan);
}

// ---------------------------------------------------------
// YARDIMCI YEREL METOTLAR (Non-static: dış kapsamdaki const/local değişkenleri
// closure ile yakalayabilmek için CS8421 hatasından kaçınmak amacıyla static değildir)
// ---------------------------------------------------------

async Task<bool> TryReconnectAsync()
{
    for (int attempt = 1; attempt <= ReconnectMaxAttempts; attempt++)
    {
        if (cts.Token.IsCancellationRequested) return false;

        Log($"Yeniden bağlanılıyor... Deneme {attempt}/{ReconnectMaxAttempts}", ConsoleColor.Yellow);

        try
        {
            await Task.Delay(ReconnectDelayMs, cts.Token);
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