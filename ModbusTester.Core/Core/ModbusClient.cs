using System.Buffers;
using System.Net.Sockets;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

namespace ModbusTester.Core.Core
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

        public Task ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadHoldingRegisters, slaveId, startAddress, destination);

        public Task ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadInputRegisters, slaveId, startAddress, destination);

        public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadCoils, slaveId, startAddress, quantity);

        public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadDiscreteInputs, slaveId, startAddress, quantity);

        // FC03/FC04 tek seferde en fazla 125 register okuyabilir; bu sınırı aşan istekler
        // otomatik olarak ardışık parçalara (chunk) bölünüp sırayla okunur ve tek bir dizide birleştirilir.
        private const int MaxRegistersPerRequest = 125;

        private async Task ReadRegistersInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort[] destination)
        {
            if (destination == null || destination.Length == 0)
                throw new ArgumentException("Hedef tampon (destination) boş veya null olamaz.", nameof(destination));

            int remaining = destination.Length;
            int destinationOffset = 0;

            while (remaining > 0)
            {
                int chunkSize = Math.Min(remaining, MaxRegistersPerRequest);
                ushort chunkStartAddress = (ushort)(startAddress + destinationOffset);

                ushort transactionId = _transactionManager.GetNextTransactionId();
                byte[] request = ModbusRequestBuilder.BuildReadRequest(
                    functionCode, transactionId, slaveId, chunkStartAddress, (ushort)chunkSize);

                byte[] response = await SendAndReceiveAsync(request);

                // response.AsSpan() ve destination.AsSpan(...) burada birer LOCAL DEĞİŞKENE
                // atanmadan, doğrudan tek bir senkron çağrının argümanı olarak üretiliyor; hiçbir
                // await sınırını geçmedikleri için CS4012'yi tetiklemezler.
                ModbusResponseParser.ParseReadRegistersResponse(
                    response, destination.AsSpan(destinationOffset, chunkSize), transactionId);

                destinationOffset += chunkSize;
                remaining -= chunkSize;
            }
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

            await _networkSemaphore.WaitAsync();

            byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(260);

            try
            {
                using var cts = new CancellationTokenSource(IoTimeoutMs);

                await _networkStream!.WriteAsync(requestBuffer, 0, requestBuffer.Length, cts.Token);

                await ReadExactAsync(rentBuffer, 0, 6, cts.Token);

                ushort remainingLength = (ushort)((rentBuffer[4] << 8) | rentBuffer[5]);

                if (remainingLength < 2 || remainingLength > 254)
                {
                    // Akış hizası tamamen kaybolduğu için soketi derhal kapatıyoruz.
                    Disconnect();

                    // Hata tipi ModbusConnectionException: bu bir cihaz-seviyesi protokol hatası değil,
                    // bizim stream'i güvenilir okuyamadığımızın kanıtı olan bir bağlantı bütünlüğü sorunudur.
                    // Üst katmanların (WinForms/Console) bunu ModbusTimeoutException/ModbusConnectionException
                    // ile aynı kefeye koyup TryReconnectAsync'i O AN, gecikmesiz tetiklemesini sağlar.
                    throw new ModbusConnectionException(
                        $"Ağ protokol ihlali: Geçersiz paket uzunluğu algılandı ({remainingLength} byte). Akış senkronizasyonu kaybolduğu için bağlantı sonlandırıldı."
                    );
                }

                await ReadExactAsync(rentBuffer, 6, remainingLength, cts.Token);

                byte[] fullResponse = new byte[6 + remainingLength];
                Buffer.BlockCopy(rentBuffer, 0, fullResponse, 0, fullResponse.Length);

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
            catch (ModbusConnectionException)
            {
                // Guard clause'dan gelen (Disconnect zaten çağrılmış) ModbusConnectionException'ı
                // olduğu gibi yukarı taşıyoruz; tekrar Disconnect() çağırıp yeniden sarmalamaya gerek yok.
                throw;
            }
            catch (ModbusProtocolException)
            {
                // Yalnızca cihazdan gelen meşru Modbus exception yanıtları (0x01-0x0B) için ayrılmıştır;
                // bağlantı sağlam kaldığı için Disconnect() çağrılmadan olduğu gibi yukarı taşınır.
                throw;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new ModbusConnectionException($"Beklenmeyen veri alışverişi hatası: {ex.Message}", ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentBuffer);
                _networkSemaphore.Release();
            }
        }

        /// <summary>
        /// NetworkStream'den tam olarak istenen sayıda byte'ı, dışarıdan verilen tampona (buffer)
        /// belirtilen offset'ten itibaren okur. Artık kendi dizisini tahsis etmiyor; çağıran taraf
        /// (SendAndReceiveAsync) havuzdan kiraladığı tamponu bu metoda geçiriyor.
        /// </summary>
        private async Task ReadExactAsync(byte[] buffer, int offset, int byteCount, CancellationToken token)
        {
            int totalRead = 0;

            while (totalRead < byteCount)
            {
                int read = await _networkStream!.ReadAsync(buffer, offset + totalRead, byteCount - totalRead, token);

                if (read == 0)
                {
                    throw new ModbusConnectionException("Bağlantı karşı taraf tarafından beklenmedik şekilde kapatıldı.");
                }

                totalRead += read;
            }
        }
    }
}