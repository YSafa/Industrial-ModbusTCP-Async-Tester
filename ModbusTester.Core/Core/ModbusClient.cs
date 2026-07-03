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

        /// <summary>FC03 - Read Holding Registers.</summary>
        public Task ReadHoldingRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadHoldingRegisters, slaveId, startAddress, destination);

        /// <summary>FC04 - Read Input Registers.</summary>
        public Task ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort[] destination)
            => ReadRegistersInternalAsync(ModbusFunctionCode.ReadInputRegisters, slaveId, startAddress, destination);

        /// <summary>FC01 - Read Coils.</summary>
        public Task<bool[]> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadCoils, slaveId, startAddress, quantity);

        /// <summary>FC02 - Read Discrete Inputs.</summary>
        public Task<bool[]> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity)
            => ReadBitsInternalAsync(ModbusFunctionCode.ReadDiscreteInputs, slaveId, startAddress, quantity);

        /// <summary>
        /// Shared implementation for FC03/FC04 register reads. Writes results directly into the
        /// destination buffer supplied by the caller — no per-call array allocation. Automatically
        /// splits requests exceeding 125 registers into sequential chunks.
        /// </summary>
        private async Task ReadRegistersInternalAsync(
            ModbusFunctionCode functionCode, byte slaveId, ushort startAddress, ushort[] destination)
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

                byte[] response = await SendAndReceiveAsync(request);

                // response.AsSpan() and destination.AsSpan(...) are produced directly as
                // arguments to a single synchronous call, without being assigned to a LOCAL
                // VARIABLE, so they never cross an await boundary and do not trigger CS4012.
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
            byte[] request = ModbusRequestBuilder.BuildWriteSingleRegisterRequest(transactionId, slaveId, address, value);
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
            byte[] request = ModbusRequestBuilder.BuildWriteMultipleRegistersRequest(transactionId, slaveId, startAddress, values);
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

            // Semaphore: guarantees only one transaction can use the socket at a time.
            await _networkSemaphore.WaitAsync();

            // Rented from the pool to cover the maximum Modbus TCP ADU size
            // (MBAP header 6 + Unit ID 1 + PDU max 253 = 260 bytes); avoids a new array
            // allocation on every transaction.
            byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(260);

            try
            {
                using var cts = new CancellationTokenSource(IoTimeoutMs);

                await _networkStream!.WriteAsync(requestBuffer, 0, requestBuffer.Length, cts.Token);

                // The first 6 bytes of the MBAP header are read directly into the start of the rented buffer.
                await ReadExactAsync(rentBuffer, 0, 6, cts.Token);

                // Length field is Big-Endian: MSB first, LSB second.
                ushort remainingLength = (ushort)((rentBuffer[4] << 8) | rentBuffer[5]);

                // Guard Clause: validated before continuing with socket I/O. Otherwise, a
                // corrupted/noisy packet could attempt to exceed the bounds of the rented
                // 260-byte buffer and throw an ArgumentOutOfRangeException. Instead, we detect
                // this here and reset the socket cleanly — trying to guess how many bytes to
                // discard from the stream based on a corrupted length field is unsafe; the only
                // reliable option once stream alignment is lost is to close and reconnect.
                if (remainingLength < 2 || remainingLength > 254)
                {
                    Disconnect();

                    // Deliberately a ModbusConnectionException, not a ModbusProtocolException:
                    // this is not a legitimate device-level protocol error but evidence that we
                    // could no longer reliably read the stream — a transport integrity issue.
                    // This lets upstream layers (WinForms/Console) trigger reconnect logic
                    // immediately, in the same tick, instead of one cycle later.
                    throw new ModbusConnectionException(
                        $"Network protocol violation: invalid packet length detected ({remainingLength} bytes). Connection closed due to lost stream synchronization.");
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
                    $"Send/receive operation did not complete within {IoTimeoutMs} ms.");
            }
            catch (SocketException socketEx)
            {
                Disconnect();
                throw new ModbusConnectionException($"Network I/O error: {socketEx.Message}", socketEx);
            }
            catch (ModbusConnectionException)
            {
                // Re-thrown as-is from the guard clause above (Disconnect already called);
                // no need to wrap it again.
                throw;
            }
            catch (ModbusProtocolException)
            {
                // Reserved for legitimate device-level Modbus exception responses (0x01-0x0B);
                // the connection stays alive, so it is passed up unchanged without disconnecting.
                throw;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new ModbusConnectionException($"Unexpected send/receive error: {ex.Message}", ex);
            }
            finally
            {
                // The rented buffer is always returned to the pool, success or failure.
                ArrayPool<byte>.Shared.Return(rentBuffer);
                _networkSemaphore.Release();
            }
        }

        /// <summary>
        /// Reads exactly the requested number of bytes from the NetworkStream into the caller's
        /// buffer, starting at the given offset. TCP may deliver data in fragments, so a single
        /// ReadAsync call may not be sufficient; this loop continues until the requested amount
        /// has been read in full.
        /// </summary>
        private async Task ReadExactAsync(byte[] buffer, int offset, int byteCount, CancellationToken token)
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
        }
    }
}