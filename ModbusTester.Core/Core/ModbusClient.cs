using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ModbusTester.Core.Exceptions;
using ModbusTester.Core.Protocol;

namespace ModbusTester.Core.Core
{
    /// <summary>
    /// Manages connecting to, disconnecting from, and reading/writing data with a Modbus TCP
    /// slave device.
    /// </summary>
    public class ModbusClient
    {
        // Modbus TCP allows a maximum of 125 registers per single Read Holding/Input Registers
        // request (FC03/FC04). Requests exceeding this limit are automatically split into
        // sequential chunks and stitched back together.
        private const int MaxRegistersPerRequest = 125;

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
        // CONNECTION MANAGEMENT
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
                        $"Timed out connecting to '{_ipAddress}:{_port}' ({ConnectTimeoutMs} ms).");
                }

                await connectTask;

                _networkStream = _tcpClient.GetStream();
            }
            catch (SocketException socketEx)
            {
                throw new ModbusConnectionException(
                    $"Could not connect to '{_ipAddress}:{_port}': {socketEx.Message}", socketEx);
            }
            catch (ModbusTimeoutException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ModbusConnectionException($"Unexpected error while connecting: {ex.Message}", ex);
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
                throw new ModbusConnectionException($"Error while closing the connection: {ex.Message}", ex);
            }
            finally
            {
                _networkStream = null;
                _tcpClient = null;
            }
        }

        // ---------------------------------------------------------
        // READ FUNCTIONS (FC01 / FC02 / FC03 / FC04)
        // ---------------------------------------------------------

        public Task ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination, int? timeoutMsOverride = null)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadHoldingRegisters, slaveId, startAddress, destination, timeoutMsOverride);

        public Task ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination, int? timeoutMsOverride = null)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadInputRegisters, slaveId, startAddress, destination, timeoutMsOverride);

        public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity, int? timeoutMsOverride = null)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadCoils, slaveId, startAddress, quantity, timeoutMsOverride);

        public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity, int? timeoutMsOverride = null)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadDiscreteInputs, slaveId, startAddress, quantity, timeoutMsOverride);

        private async Task ReadRegistersInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort[] destination, int? timeoutMsOverride)
        {
            if (destination == null || destination.Length == 0)
                throw new ArgumentException("Destination buffer cannot be null or empty.", nameof(destination));

            int remaining = destination.Length;
            int destinationOffset = 0;

            while (remaining > 0)
            {
                int chunkSize = Math.Min(remaining, MaxRegistersPerRequest);
                ushort chunkStartAddress = (ushort)(startAddress + destinationOffset);

                ushort transactionId = _transactionManager.GetNextTransactionId();
                byte[] request = ModbusRequestBuilder.BuildReadRequest(
                    functionCode, transactionId, slaveId, chunkStartAddress, (ushort)chunkSize);

                byte[] response = await SendAndReceiveAsync(request, timeoutMsOverride);

                ModbusResponseParser.ParseReadRegistersResponse(
                    response, destination.AsSpan(destinationOffset, chunkSize), transactionId);

                destinationOffset += chunkSize;
                remaining -= chunkSize;
            }
        }

        private async Task<bool[]> ReadBitsInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort quantity, int? timeoutMsOverride)
        {
            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request = ModbusRequestBuilder.BuildReadRequest(functionCode, transactionId, slaveId, startAddress, quantity);
            byte[] response = await SendAndReceiveAsync(request, timeoutMsOverride);
            return ModbusResponseParser.ParseReadBitsResponse(response, transactionId, quantity);
        }

        internal async Task<byte[]> SendAndReceiveAsync(byte[] requestBuffer, int? timeoutMsOverride = null)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new ModbusConnectionException("Cannot send request: no active connection.");
            }

            await _networkSemaphore.WaitAsync();

            byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(260);
            int bytesReceivedThisTransaction = 0;

            // Allows callers to request a SHORTER timeout for a specific call (e.g. a quick "probe"
            // retry against a slave already known to be unreliable), without affecting the
            // connection's normal IoTimeoutMs used by every other, healthy request sharing this
            // same pooled socket.
            int effectiveTimeoutMs = timeoutMsOverride ?? IoTimeoutMs;

            try
            {
                using var cts = new CancellationTokenSource(effectiveTimeoutMs);

                await _networkStream!.WriteAsync(requestBuffer, 0, requestBuffer.Length, cts.Token);

                bytesReceivedThisTransaction += await ReadExactAsync(rentBuffer, 0, 6, cts.Token);

                ushort remainingLength = (ushort)((rentBuffer[4] << 8) | rentBuffer[5]);

                if (remainingLength < 2 || remainingLength > 254)
                {
                    Disconnect();
                    throw new ModbusConnectionException(
                        $"Network protocol violation: invalid packet length detected ({remainingLength} bytes). Connection closed due to lost stream synchronization.");
                }

                bytesReceivedThisTransaction += await ReadExactAsync(rentBuffer, 6, remainingLength, cts.Token);

                byte[] fullResponse = new byte[6 + remainingLength];
                Buffer.BlockCopy(rentBuffer, 0, fullResponse, 0, fullResponse.Length);

                return fullResponse;
            }
            catch (OperationCanceledException)
            {
                if (bytesReceivedThisTransaction > 0)
                {
                    Disconnect();
                    throw new ModbusConnectionException(
                        $"Timeout after partially receiving {bytesReceivedThisTransaction} byte(s); connection reset to prevent stream desynchronization.");
                }

                throw new ModbusTimeoutException(
                    $"No response received within {effectiveTimeoutMs} ms (device/slave may not be responding).");
            }
            catch (SocketException socketEx)
            {
                Disconnect();
                throw new ModbusConnectionException($"Network I/O error: {socketEx.Message}", socketEx);
            }
            catch (ModbusConnectionException)
            {
                throw;
            }
            catch (ModbusProtocolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new ModbusConnectionException($"Unexpected send/receive error: {ex.Message}", ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentBuffer);
                _networkSemaphore.Release();
            }
        }

        // ---------------------------------------------------------
        // WRITE FUNCTIONS (FC05 / FC06 / FC16)
        // ---------------------------------------------------------

        /// <summary>
        /// FC05 (Write Single Coil): writes True or False to a single coil.
        /// The slave echoes the sent packet back; the parser validates this echo.
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
        /// FC06 (Write Single Register): writes a 16-bit value to a single holding register.
        /// The slave echoes the sent packet back; the parser validates this echo.
        /// </summary>
        public async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value)
        {
            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request =
                ModbusRequestBuilder.BuildWriteSingleRegisterRequest(transactionId, slaveId, address, value);
            byte[] response = await SendAndReceiveAsync(request);

            ModbusResponseParser.ValidateWriteSingleResponse(
                response, transactionId, ModbusFunctionCode.WriteSingleRegister, address);
        }

        /// <summary>
        /// FC16 (Write Multiple Registers): writes a batch of values to consecutive holding
        /// registers. The slave echoes back the start address and the number of registers written.
        /// </summary>
        public async Task WriteMultipleRegistersAsync(byte slaveId, ushort startAddress, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("The register array to write cannot be empty.", nameof(values));
            }

            ushort transactionId = _transactionManager.GetNextTransactionId();
            byte[] request =
                ModbusRequestBuilder.BuildWriteMultipleRegistersRequest(transactionId, slaveId, startAddress, values);
            byte[] response = await SendAndReceiveAsync(request);

            ModbusResponseParser.ValidateWriteMultipleRegistersResponse(
                response, transactionId, startAddress, (ushort)values.Length);
        }

        // ---------------------------------------------------------
        // TRANSPORT (SEND / RECEIVE) AND HELPERS
        // ---------------------------------------------------------

        internal async Task<byte[]> SendAndReceiveAsync(byte[] requestBuffer)
        {
            if (!IsConnected || _networkStream == null)
            {
                throw new ModbusConnectionException("Cannot send request: no active connection.");
            }

            await _networkSemaphore.WaitAsync();

            byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(260);

            // Tracks how many bytes were actually received in THIS transaction, across both the
            // header and body reads. Used to distinguish a clean "slave didn't respond at all"
            // timeout (safe — stream alignment intact, socket stays open for other multiplexed
            // tabs) from a "response started arriving but never completed" timeout (dangerous —
            // stream is desynced, the shared socket must be torn down).
            int bytesReceivedThisTransaction = 0;

            try
            {
                using var cts = new CancellationTokenSource(IoTimeoutMs);

                await _networkStream!.WriteAsync(requestBuffer, 0, requestBuffer.Length, cts.Token);

                bytesReceivedThisTransaction += await ReadExactAsync(rentBuffer, 0, 6, cts.Token);

                ushort remainingLength = (ushort)((rentBuffer[4] << 8) | rentBuffer[5]);

                if (remainingLength < 2 || remainingLength > 254)
                {
                    Disconnect();
                    throw new ModbusConnectionException(
                        $"Network protocol violation: invalid packet length detected ({remainingLength} bytes). Connection closed due to lost stream synchronization.");
                }

                bytesReceivedThisTransaction += await ReadExactAsync(rentBuffer, 6, remainingLength, cts.Token);

                byte[] fullResponse = new byte[6 + remainingLength];
                Buffer.BlockCopy(rentBuffer, 0, fullResponse, 0, fullResponse.Length);

                return fullResponse;
            }
            catch (OperationCanceledException)
            {
                if (bytesReceivedThisTransaction > 0)
                {
                    // Partial data was received before the timeout hit — the stream is now
                    // misaligned for the next transaction. This is a genuine transport integrity
                    // failure, not just a quiet slave; the shared socket must be reset.
                    Disconnect();
                    throw new ModbusConnectionException(
                        $"Timeout after partially receiving {bytesReceivedThisTransaction} byte(s); connection reset to prevent stream desynchronization.");
                }

                // Zero bytes were received: nothing was consumed from the stream, so it remains
                // perfectly aligned. This is a clean "this device/slave didn't answer in time"
                // timeout — critical to NOT disconnect here, since in a multiplexed pool this
                // same socket may be serving other, perfectly healthy slave IDs.
                throw new ModbusTimeoutException(
                    $"No response received within {IoTimeoutMs} ms (device/slave may not be responding).");
            }
            catch (SocketException socketEx)
            {
                Disconnect();
                throw new ModbusConnectionException($"Network I/O error: {socketEx.Message}", socketEx);
            }
            catch (ModbusConnectionException)
            {
                throw;
            }
            catch (ModbusProtocolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new ModbusConnectionException($"Unexpected send/receive error: {ex.Message}", ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentBuffer);
                _networkSemaphore.Release();
            }
        }

        /// <summary>
        /// Reads exactly the requested number of bytes into the caller's buffer. Returns the number
        /// of bytes successfully read before either completing or being cancelled — this lets the
        /// caller distinguish "timed out with zero bytes received" (stream still aligned, safe) from
        /// "timed out mid-read" (stream desynced, socket must be reset), without using a ref/out
        /// parameter, which async methods cannot have (CS1988).
        /// </summary>
        private async Task<int> ReadExactAsync(byte[] buffer, int offset, int byteCount, CancellationToken token)
        {
            int totalRead = 0;

            while (totalRead < byteCount)
            {
                int read = await _networkStream!.ReadAsync(buffer, offset + totalRead, byteCount - totalRead, token);

                if (read == 0)
                {
                    throw new ModbusConnectionException("Connection was closed unexpectedly by the remote host.");
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}