using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ModbusTester.Exceptions;
using ModbusTester.Protocol;

namespace ModbusTester.Core
{
    public class ModbusClient
    {
        // Nullable Reference Types aktif; tüm referans alanlar açıkça null-atanabilir tanımlandı.
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;

        private readonly string _ipAddress;
        private readonly int _port;

        private readonly ModbusTransactionManager _transactionManager = new ModbusTransactionManager();
        private readonly SemaphoreSlim _networkSemaphore = new SemaphoreSlim(1, 1);

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        public int ConnectTimeoutMs { get; set; } = 3000;
        public int IoTimeoutMs { get; set; } = 3000;

        public ModbusClient(string ipAddress, int port = 502)
        {
            _ipAddress = ipAddress;
            _port = port;
        }

        // ---------------------------------------------------------
        // BAĞLANTI YÖNETİMİ
        // ---------------------------------------------------------

        public async Task ConnectAsync()
        {
            try
            {
                _tcpClient = new TcpClient();

                using var cts = new CancellationTokenSource(ConnectTimeoutMs);
                var connectTask = _tcpClient.ConnectAsync(_ipAddress, _port);
                var timeoutTask = Task.Delay(Timeout.Infinite, cts.Token);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask != connectTask)
                {
                    throw new ModbusTimeoutException(
                        $"'{_ipAddress}:{_port}' adresine bağlanırken zaman aşımı oluştu ({ConnectTimeoutMs} ms).");
                }

                await connectTask;

                _networkStream = _tcpClient.GetStream();
            }
            catch (SocketException socketEx)
            {
                throw new ModbusConnectionException(
                    $"'{_ipAddress}:{_port}' adresine bağlanılamadı: {socketEx.Message}", socketEx);
            }
            catch (ModbusTimeoutException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ModbusConnectionException($"Bağlantı sırasında beklenmeyen hata: {ex.Message}", ex);
            }
        }

        public void Disconnect()
        {
            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception ex)
            {
                throw new ModbusConnectionException($"Bağlantı kapatılırken hata oluştu: {ex.Message}", ex);
            }
            finally
            {
                _networkStream = null;
                _tcpClient = null;
            }
        }

        // ---------------------------------------------------------
        // OKUMA FONKSİYONLARI (FC01 / FC02 / FC03 / FC04)
        // ---------------------------------------------------------

        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadHoldingRegisters, slaveId, startAddress, quantity);

        public Task<ushort[]> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadInputRegisters, slaveId, startAddress, quantity);

        public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadCoils, slaveId, startAddress, quantity);

        public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadDiscreteInputs, slaveId, startAddress, quantity);

        // FC03/FC04 tek seferde en fazla 125 register okuyabilir; bu sınırı aşan istekler
        // otomatik olarak ardışık parçalara (chunk) bölünüp sırayla okunur ve tek bir dizide birleştirilir.
        private const int MaxRegistersPerRequest = 125;

        private async Task<ushort[]> ReadRegistersInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort quantity)
        {
            if (quantity == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "Okunacak register sayısı 0 olamaz.");
            }

            // Sonuçların birleştirileceği tek hedef dizi; toplam miktar kadar önceden ayrılır.
            ushort[] destination = new ushort[quantity];

            int remaining = quantity;
            int destinationOffset = 0;

            while (remaining > 0)
            {
                // Bu turda okunacak parça boyutu: kalan miktar 125'i aşıyorsa 125, aşmıyorsa kalanın tamamı.
                int chunkSize = Math.Min(remaining, MaxRegistersPerRequest);

                // Bu parçanın başlangıç adresi: ana başlangıç adresine, şimdiye kadar okunan register sayısı eklenir.
                ushort chunkStartAddress = (ushort)(startAddress + destinationOffset);

                ushort transactionId = _transactionManager.GetNextTransactionId();
                byte[] request = ModbusRequestBuilder.BuildReadRequest(
                    functionCode, transactionId, slaveId, chunkStartAddress, (ushort)chunkSize);

                // SendAndReceiveAsync zaten _networkSemaphore ile korunuyor; burada ekstra kilit gerekmiyor.
                // Herhangi bir chunk hata fırlatırsa (Timeout/Connection), exception olduğu gibi yukarı taşınır
                // ve şimdiye kadar toplanan kısmi veri hiçbir şekilde döndürülmez (fault isolation).
                byte[] response = await SendAndReceiveAsync(request);
                ushort[] chunkData = ModbusResponseParser.ParseReadRegistersResponse(response, transactionId);

                // KESİN DOĞRULAMA: Cihazın eksik veya hatalı boyutta veri dönme ihtimalini sınırda yakala.
                // Array.Copy'nin örtük ArgumentException'ına güvenmek yerine, burada net bir Modbus seviyesi
                // hata fırlatıyoruz ki üst katman (MainForm) bunu diğer protokol hataları gibi tutarlı işleyebilsin.
                if (chunkData.Length != chunkSize)
                {
                    throw new ModbusProtocolException(
                        0x04, // Slave Device Failure (Donanım Hatası kodu)
                        $"Donanımdan eksik veri paketi geldi. Beklenen: {chunkSize}, Gelen: {chunkData.Length}"
                    );
                }

                // Artık bellek sınırlarından %100 emin olarak kopyalayabiliriz.
                Array.Copy(chunkData, 0, destination, destinationOffset, chunkData.Length);

                destinationOffset += chunkSize;
                remaining -= chunkSize;
            }

            return destination;
        }

        private async Task<bool[]> ReadBitsInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort quantity)
        {
            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request = ModbusRequestBuilder.BuildReadRequest(functionCode, transactionId, slaveId, startAddress, quantity);
            byte[] response = await SendAndReceiveAsync(request);
            return ModbusResponseParser.ParseReadBitsResponse(response, transactionId, quantity);
        }

        // ---------------------------------------------------------
        // YAZMA FONKSİYONLARI (FC05 / FC06 / FC16)
        // ---------------------------------------------------------

        /// <summary>
        /// FC05 (Write Single Coil): Tek bir coil'e True veya False yazar.
        /// Slave gönderilen paketi echo olarak geri döndürür; parser bu echo'yu doğrular.
        /// </summary>
        public async Task WriteSingleCoilAsync(byte slaveId, ushort address, bool value)
        {
            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request = ModbusRequestBuilder.BuildWriteSingleCoilRequest(transactionId, slaveId, address, value);
            byte[] response = await SendAndReceiveAsync(request);

            ModbusResponseParser.ValidateWriteSingleResponse(
                response, transactionId, ModbusFunctionCode.WriteSingleCoil, address);
        }

        /// <summary>
        /// FC06 (Write Single Register): Tek bir holding register'a 16-bit değer yazar.
        /// Slave gönderilen paketi echo olarak geri döndürür; parser bu echo'yu doğrular.
        /// </summary>
        public async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value)
        {
            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request = ModbusRequestBuilder.BuildWriteSingleRegisterRequest(transactionId, slaveId, address, value);
            byte[] response = await SendAndReceiveAsync(request);

            ModbusResponseParser.ValidateWriteSingleResponse(
                response, transactionId, ModbusFunctionCode.WriteSingleRegister, address);
        }

        /// <summary>
        /// FC16 (Write Multiple Registers): Ardışık birden fazla holding register'a toplu değer yazar.
        /// Slave başlangıç adresi ve yazılan register sayısını echo olarak geri döndürür.
        /// </summary>
        public async Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("Yazılacak register dizisi boş olamaz.", nameof(values));
            }

            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request = ModbusRequestBuilder.BuildWriteMultipleRegistersRequest(transactionId, slaveId, startAddress, values);
            byte[] response = await SendAndReceiveAsync(request);

            ModbusResponseParser.ValidateWriteMultipleRegistersResponse(
                response, transactionId, startAddress, (ushort)values.Length);
        }

        // ---------------------------------------------------------
        // AĞIRLAMA (SEND / RECEIVE) ve YARDIMCI METOTLAR
        // ---------------------------------------------------------

        internal async Task<byte[]> SendAndReceiveAsync(byte[] requestBuffer)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new ModbusConnectionException("İstek gönderilemedi: aktif bir bağlantı yok.");
            }

            // Semaphore: aynı anda yalnızca bir transaction soketi kullanabilir.
            await _networkSemaphore.WaitAsync();

            try
            {
                using var cts = new CancellationTokenSource(IoTimeoutMs);

                // Null-forgiving (!) operatörü: bu noktada yukarıdaki guard clause'u geçtiğimiz için
                // _networkStream kesinlikle null değildir; derleyiciye bunu bildiriyoruz.
                await _networkStream!.WriteAsync(requestBuffer, 0, requestBuffer.Length, cts.Token);

                byte[] header = await ReadExactAsync(6, cts.Token);

                // MBAP Length alanı Big-Endian: kalan byte sayısını bit kaydırmayla hesaplıyoruz.
                ushort remainingLength = (ushort)((header[4] << 8) | header[5]);

                byte[] remainingBody = await ReadExactAsync(remainingLength, cts.Token);

                byte[] fullResponse = new byte[6 + remainingBody.Length];
                Buffer.BlockCopy(header, 0, fullResponse, 0, header.Length);
                Buffer.BlockCopy(remainingBody, 0, fullResponse, header.Length, remainingBody.Length);

                return fullResponse;
            }
            catch (OperationCanceledException)
            {
                Disconnect();
                throw new ModbusTimeoutException(
                    $"Veri gönderme/alma işlemi {IoTimeoutMs} ms içinde tamamlanamadı.");
            }
            catch (SocketException socketEx)
            {
                Disconnect();
                throw new ModbusConnectionException($"Ağ üzerinden veri alışverişinde hata: {socketEx.Message}", socketEx);
            }
            catch (ModbusProtocolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new ModbusConnectionException($"Beklenmeyen veri alışverişi hatası: {ex.Message}", ex);
            }
            finally
            {
                // Başarı veya hata fark etmeksizin semaphore her koşulda serbest bırakılır.
                _networkSemaphore.Release();
            }
        }

        private async Task<byte[]> ReadExactAsync(int byteCount, CancellationToken token)
        {
            byte[] buffer = new byte[byteCount];
            int totalRead = 0;

            while (totalRead < byteCount)
            {
                int read = await _networkStream!.ReadAsync(buffer, totalRead, byteCount - totalRead, token);

                if (read == 0)
                {
                    throw new ModbusConnectionException(
                        "Bağlantı karşı taraf tarafından beklenmedik şekilde kapatıldı.");
                }

                totalRead += read;
            }

            return buffer;
        }
    }
}