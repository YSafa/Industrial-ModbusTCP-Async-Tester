using System;

namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Builds Modbus TCP request packets (MBAP header + PDU) as byte arrays.
    /// Supports read functions (FC01/02/03/04) and write functions (FC05/06/16).
    /// </summary>
    public static class ModbusRequestBuilder
    {
        // ---------------------------------------------------------
        // READ FUNCTIONS (FC01 / FC02 / FC03 / FC04)
        // ---------------------------------------------------------

        /// <summary>
        /// Builds the common 12-byte request packet for FC01 (Read Coils), FC02 (Read Discrete
        /// Inputs), FC03 (Read Holding Registers), and FC04 (Read Input Registers).
        ///
        /// Packet layout:
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length
        /// [6] Unit ID | [7] Function Code | [8-9] Start Address | [10-11] Quantity
        /// </summary>
        public static byte[] BuildReadRequest(
            ModbusFunctionCode functionCode, ushort transactionId, byte slaveId,
            ushort startAddress, ushort quantity)
        {
            int maxQuantity = (functionCode == ModbusFunctionCode.ReadCoils ||
                               functionCode == ModbusFunctionCode.ReadDiscreteInputs) ? 2000 : 125;

            if (quantity < 1 || quantity > maxQuantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity), $"Quantity must be between 1 and {maxQuantity}.");
            }

            byte[] request = new byte[12];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, 6); // Unit ID + FC + Address(2) + Quantity(2) = 6 bytes
            request[6] = slaveId;
            request[7] = (byte)functionCode;
            WriteUInt16BigEndian(request, 8, startAddress);
            WriteUInt16BigEndian(request, 10, quantity);

            return request;
        }

        // ---------------------------------------------------------
        // FC05 — WRITE SINGLE COIL
        // ---------------------------------------------------------

        /// <summary>
        /// Builds the 12-byte request packet for FC05 (Write Single Coil).
        ///
        /// Packet layout:
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (fixed 6)
        /// [6] Unit ID | [7] 0x05 | [8-9] Coil Address | [10-11] Value (0xFF00 or 0x0000)
        ///
        /// The Modbus protocol accepts only 0xFF00 (True) and 0x0000 (False) as valid coil
        /// write values; any other value causes the slave to return Illegal Data Value (0x03).
        /// </summary>
        public static byte[] BuildWriteSingleCoilRequest(
            ushort transactionId, byte slaveId, ushort address, bool value)
        {
            byte[] request = new byte[12];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, 6);
            request[6] = slaveId;
            request[7] = (byte)ModbusFunctionCode.WriteSingleCoil;
            WriteUInt16BigEndian(request, 8, address);

            // True -> 0xFF00, False -> 0x0000: these are the only two values the protocol accepts.
            request[10] = value ? (byte)0xFF : (byte)0x00;
            request[11] = 0x00;

            return request;
        }

        // ---------------------------------------------------------
        // FC06 — WRITE SINGLE REGISTER
        // ---------------------------------------------------------

        /// <summary>
        /// Builds the 12-byte request packet for FC06 (Write Single Register).
        ///
        /// Packet layout:
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (fixed 6)
        /// [6] Unit ID | [7] 0x06 | [8-9] Register Address | [10-11] Register Value (Big-Endian)
        /// </summary>
        public static byte[] BuildWriteSingleRegisterRequest(
            ushort transactionId, byte slaveId, ushort address, ushort value)
        {
            byte[] request = new byte[12];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, 6);
            request[6] = slaveId;
            request[7] = (byte)ModbusFunctionCode.WriteSingleRegister;
            WriteUInt16BigEndian(request, 8, address);
            WriteUInt16BigEndian(request, 10, value); // 16-bit value to write, Big-Endian encoded.

            return request;
        }

        // ---------------------------------------------------------
        // FC16 — WRITE MULTIPLE REGISTERS
        // ---------------------------------------------------------

        /// <summary>
        /// Builds the variable-length request packet for FC16 (Write Multiple Registers).
        ///
        /// Packet layout (6-byte MBAP header + variable PDU):
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (variable)
        /// [6] Unit ID | [7] 0x10 | [8-9] Start Address | [10-11] Register Count
        /// [12] Byte Count (register count * 2) | [13...] Register data (Big-Endian)
        ///
        /// For FC16, the MBAP Length field is calculated as:
        /// UnitID(1) + FC(1) + StartAddr(2) + Qty(2) + ByteCount(1) + 2 bytes per register
        /// = 7 + (registerCount * 2).
        /// </summary>
        public static byte[] BuildWriteMultipleRegistersRequest(
            ushort transactionId, byte slaveId, ushort startAddress, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException(
                    "The register array to write cannot be null or empty.", nameof(values));
            }

            if (values.Length > 123)
            {
                // A maximum of 123 registers can be written in a single request (protocol limit).
                throw new ArgumentOutOfRangeException(
                    nameof(values), "A maximum of 123 registers can be written in a single request.");
            }

            int registerCount = values.Length;
            int byteCount = registerCount * 2;         // Each register occupies 2 bytes.
            int pduLength = 1 + 1 + 2 + 2 + 1 + byteCount; // Unit ID + FC + StartAddr + Qty + ByteCount + data
            int totalPacketLength = 6 + pduLength;     // MBAP Header (6) + PDU

            byte[] request = new byte[totalPacketLength];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, (ushort)pduLength); // MBAP Length field: bytes remaining, including Unit ID.
            request[6] = slaveId;
            request[7] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
            WriteUInt16BigEndian(request, 8, startAddress);
            WriteUInt16BigEndian(request, 10, (ushort)registerCount);
            request[12] = (byte)byteCount; // Byte Count field in the PDU; tells the slave how many data bytes follow.

            // Write register values into the packet in Big-Endian order.
            for (int i = 0; i < registerCount; i++)
            {
                int offset = 13 + (i * 2);
                WriteUInt16BigEndian(request, offset, values[i]);
            }

            return request;
        }

        // ---------------------------------------------------------
        // SHARED HELPER METHODS
        // ---------------------------------------------------------

        /// Writes the Transaction ID in Big-Endian order at the given offset.
        private static void WriteTransactionId(byte[] buffer, int offset, ushort transactionId)
        {
            buffer[offset]     = (byte)(transactionId >> 8);
            buffer[offset + 1] = (byte)(transactionId & 0xFF);
        }

        /// Writes the fixed Modbus TCP Protocol ID (0x0000) value.
        private static void WriteProtocolId(byte[] buffer, int offset)
        {
            buffer[offset]     = 0x00;
            buffer[offset + 1] = 0x00;
        }

        /// Writes the MBAP Length field (byte count following this field) in Big-Endian order.
        private static void WriteLength(byte[] buffer, int offset, ushort length)
        {
            buffer[offset]     = (byte)(length >> 8);
            buffer[offset + 1] = (byte)(length & 0xFF);
        }

        /// Writes any 16-bit value at the given offset in Big-Endian order.
        private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset]     = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }
    }
}