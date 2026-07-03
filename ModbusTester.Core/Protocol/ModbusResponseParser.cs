using ModbusTester.Core.Exceptions;

namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Validates Modbus TCP response packets and decodes them into usable data types.
    /// Supports read (FC01/02/03/04) and write (FC05/06/16) response validation.
    /// </summary>
    public static class ModbusResponseParser
    {
        // ---------------------------------------------------------
        // REGISTER READ PARSING (ALLOCATION-FREE)
        // ---------------------------------------------------------

        /// <summary>
        /// Parses an FC03 (Read Holding Registers) / FC04 (Read Input Registers) response and
        /// writes the decoded values directly into the caller-supplied destination buffer.
        /// Does not allocate a new array; response is accepted as a ReadOnlySpan for performance.
        /// </summary>
        public static void ParseReadRegistersResponse(ReadOnlySpan<byte> response, Span<ushort> destination, ushort expectedTransactionId)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);

            byte byteCount = response[8];
            int registerCount = byteCount / 2;

            // Safety check: if the register count reported by the slave doesn't match the size
            // of the buffer the caller allocated (e.g. a corrupted/unexpected response), we fail
            // fast here with a clear protocol error before ever touching the Span bounds.
            if (registerCount != destination.Length)
            {
                throw new ModbusProtocolException(0x04,
                    $"Register count in response does not match expected. Expected: {destination.Length}, Received: {registerCount}.");
            }

            for (int i = 0; i < registerCount; i++)
            {
                int offset = 9 + (i * 2);
                destination[i] = (ushort)((response[offset] << 8) | response[offset + 1]);
            }
        }

        /// <summary>
        /// Parses an FC01 (Read Coils) / FC02 (Read Discrete Inputs) response. Data arrives
        /// bit-packed within bytes and is unpacked starting from the LSB of each byte.
        /// </summary>
        public static bool[] ParseReadBitsResponse(byte[] response, ushort expectedTransactionId, int requestedQuantity)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);

            byte byteCount = response[8];
            bool[] bits = new bool[requestedQuantity];

            for (int i = 0; i < requestedQuantity; i++)
            {
                int byteIndex = i / 8;
                int bitIndex  = i % 8;

                if (byteIndex >= byteCount) break;

                // Right-shift and mask the lowest bit (& 0x01) to read the state of that bit.
                bits[i] = ((response[9 + byteIndex] >> bitIndex) & 0x01) == 1;
            }

            return bits;
        }

        // ---------------------------------------------------------
        // WRITE RESPONSE VALIDATION
        // ---------------------------------------------------------

        /// <summary>
        /// Validates an FC05 (Write Single Coil) / FC06 (Write Single Register) response.
        /// These functions return an "echo" response: the request is reflected back as-is.
        /// Validates: Transaction ID + exception check + Function Code + address match.
        /// </summary>
        public static void ValidateWriteSingleResponse(
            byte[] response, ushort expectedTransactionId,
            ModbusFunctionCode expectedFunctionCode, ushort expectedAddress)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);
            ValidateFunctionCode(response, expectedFunctionCode);

            ushort echoAddress = (ushort)((response[8] << 8) | response[9]);

            if (echoAddress != expectedAddress)
            {
                throw new ModbusProtocolException(0,
                    $"Address in write response does not match. Expected: {expectedAddress}, Received: {echoAddress}.");
            }
        }

        /// <summary>
        /// Validates an FC16 (Write Multiple Registers) response.
        /// The FC16 echo is shorter (12 bytes): the slave returns only the start address and
        /// the register count it wrote (not the full data payload).
        /// </summary>
        public static void ValidateWriteMultipleRegistersResponse(
            byte[] response, ushort expectedTransactionId,
            ushort expectedAddress, ushort expectedRegisterCount)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);
            ValidateFunctionCode(response, ModbusFunctionCode.WriteMultipleRegisters);

            ushort echoAddress = (ushort)((response[8] << 8) | response[9]);

            if (echoAddress != expectedAddress)
            {
                throw new ModbusProtocolException(0,
                    $"Address in FC16 response does not match. Expected: {expectedAddress}, Received: {echoAddress}.");
            }

            ushort echoQuantity = (ushort)((response[10] << 8) | response[11]);

            if (echoQuantity != expectedRegisterCount)
            {
                throw new ModbusProtocolException(0,
                    $"Register count in FC16 response does not match. Expected: {expectedRegisterCount}, Received: {echoQuantity}.");
            }
        }

        // ---------------------------------------------------------
        // SHARED VALIDATION HELPERS
        // ---------------------------------------------------------

        private static void ValidateTransactionId(ReadOnlySpan<byte> response, ushort expectedTransactionId)
        {
            ushort receivedId = (ushort)((response[0] << 8) | response[1]);

            if (receivedId != expectedTransactionId)
            {
                throw new ModbusProtocolException(0,
                    $"Transaction ID mismatch. Expected: {expectedTransactionId}, Received: {receivedId}.");
            }
        }

        private static void ValidateNotException(ReadOnlySpan<byte> response)
        {
            byte functionCode = response[7];

            // When the slave signals an error, bit 7 (MSB) of the function code is set
            // (e.g. 0x03 becomes 0x83).
            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response[8];
                throw new ModbusProtocolException(exceptionCode,
                    $"{exceptionCode:D2} - {GetExceptionDescription(exceptionCode)}");
            }
        }

        private static void ValidateFunctionCode(ReadOnlySpan<byte> response, ModbusFunctionCode expected)
        {
            byte received = response[7];

            if (received != (byte)expected)
            {
                throw new ModbusProtocolException(0,
                    $"Function code mismatch. Expected: 0x{(byte)expected:X2}, Received: 0x{received:X2}.");
            }
        }

        // Kept intentionally bilingual: these are the official Modbus standard exception
        // descriptions and remain useful as a quick reference regardless of UI language.
        private static string GetExceptionDescription(byte exceptionCode)
        {
            return exceptionCode switch
            {
                0x01 => "Illegal Function (Desteklenmeyen fonksiyon kodu)",
                0x02 => "Illegal Data Address (Geçersiz veri adresi)",
                0x03 => "Illegal Data Value (Geçersiz veri değeri)",
                0x04 => "Slave Device Failure (Cihaz dahili hatası)",
                0x05 => "Acknowledge (İşlem kabul edildi, hala işleniyor)",
                0x06 => "Slave Device Busy (Cihaz meşgul)",
                0x08 => "Memory Parity Error (Bellek paritesi hatası)",
                0x0A => "Gateway Path Unavailable (Geçit yolu kullanılamıyor)",
                0x0B => "Gateway Target Device Failed to Respond (Hedef cihaz yanıt vermedi)",
                _    => "Unknown Modbus exception code"
            };
        }
    }
}