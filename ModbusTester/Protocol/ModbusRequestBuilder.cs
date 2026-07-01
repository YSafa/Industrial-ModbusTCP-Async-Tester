using System;

namespace ModbusTester.Protocol
{
    /// <summary>
    /// Modbus TCP istek paketlerini (MBAP Header + PDU) byte dizisi olarak hazırlayan sınıf.
    /// Okuma (FC01/02/03/04) ve yazma (FC05/06/16) fonksiyonlarını destekler.
    /// </summary>
    public static class ModbusRequestBuilder
    {
        // ---------------------------------------------------------
        // OKUMA FONKSİYONLARI (FC01 / FC02 / FC03 / FC04)
        // ---------------------------------------------------------

        /// <summary>
        /// FC01 (Read Coils), FC02 (Read Discrete Inputs), FC03 (Read Holding Registers) ve
        /// FC04 (Read Input Registers) için ortak 12 byte'lık istek paketini hazırlar.
        ///
        /// Paket yapısı:
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
                    nameof(quantity), $"Miktar 1 ile {maxQuantity} arasında olmalıdır.");
            }

            byte[] request = new byte[12];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, 6); // Unit ID + FC + Address(2) + Quantity(2) = 6 byte
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
        /// FC05 (Write Single Coil) için 12 byte'lık istek paketini hazırlar.
        ///
        /// Paket yapısı:
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (sabit 6)
        /// [6] Unit ID | [7] 0x05 | [8-9] Coil Address | [10-11] Value (0xFF00 veya 0x0000)
        ///
        /// Modbus protokolü coil yazma değeri olarak yalnızca 0xFF00 (True) ve 0x0000 (False) kabul eder;
        /// başka bir değer gönderilirse slave Illegal Data Value (0x03) hatası döndürür.
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

            // True -> 0xFF00, False -> 0x0000: sadece bu iki değer protokol tarafından geçerli sayılır.
            request[10] = value ? (byte)0xFF : (byte)0x00;
            request[11] = 0x00;

            return request;
        }

        // ---------------------------------------------------------
        // FC06 — WRITE SINGLE REGISTER
        // ---------------------------------------------------------

        /// <summary>
        /// FC06 (Write Single Register) için 12 byte'lık istek paketini hazırlar.
        ///
        /// Paket yapısı:
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (sabit 6)
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
            WriteUInt16BigEndian(request, 10, value); // Yazılacak 16-bit değer, Big-Endian ile paketlenir.

            return request;
        }

        // ---------------------------------------------------------
        // FC16 — WRITE MULTIPLE REGISTERS
        // ---------------------------------------------------------

        /// <summary>
        /// FC16 (Write Multiple Registers) için değişken uzunluklu istek paketini hazırlar.
        ///
        /// Paket yapısı (MBAP Header 6 byte + PDU değişken):
        /// [0-1] Transaction ID | [2-3] Protocol ID | [4-5] Length (değişken)
        /// [6] Unit ID | [7] 0x10 | [8-9] Start Address | [10-11] Register Count
        /// [12] Byte Count (register sayısı * 2) | [13...] Register verileri (Big-Endian)
        ///
        /// FC16'da MBAP Length alanı; Unit ID(1) + FC(1) + StartAddr(2) + Qty(2) + ByteCount(1)
        /// + her register için 2 byte = 7 + (registerCount * 2) olarak hesaplanır.
        /// </summary>
        public static byte[] BuildWriteMultipleRegistersRequest(
            ushort transactionId, byte slaveId, ushort startAddress, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException(
                    "Yazılacak register dizisi boş veya null olamaz.", nameof(values));
            }

            if (values.Length > 123)
            {
                // Tek seferde en fazla 123 register yazılabilir (Modbus protokol sınırı).
                throw new ArgumentOutOfRangeException(
                    nameof(values), "Tek seferde en fazla 123 register yazılabilir.");
            }

            int registerCount = values.Length;
            int byteCount = registerCount * 2;         // Her register 2 byte yer kaplar.
            int pduLength = 1 + 1 + 2 + 2 + 1 + byteCount; // Unit ID + FC + StartAddr + Qty + ByteCount + data
            int totalPacketLength = 6 + pduLength;     // MBAP Header (6) + PDU

            byte[] request = new byte[totalPacketLength];

            WriteTransactionId(request, 0, transactionId);
            WriteProtocolId(request, 2);
            WriteLength(request, 4, (ushort)pduLength); // MBAP Length alanı: Unit ID dahil kalan byte sayısı.
            request[6] = slaveId;
            request[7] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
            WriteUInt16BigEndian(request, 8, startAddress);
            WriteUInt16BigEndian(request, 10, (ushort)registerCount);
            request[12] = (byte)byteCount; // PDU'daki Byte Count alanı; kaç byte veri geleceğini slave'e bildirir.

            // Register değerlerini Big-Endian sırasıyla pakete yazıyoruz.
            for (int i = 0; i < registerCount; i++)
            {
                int offset = 13 + (i * 2);
                WriteUInt16BigEndian(request, offset, values[i]);
            }

            return request;
        }

        // ---------------------------------------------------------
        // ORTAK YARDIMCI METOTLAR
        // ---------------------------------------------------------

        /// Transaction ID'yi Big-Endian olarak belirtilen offset'e yazar.
        private static void WriteTransactionId(byte[] buffer, int offset, ushort transactionId)
        {
            buffer[offset]     = (byte)(transactionId >> 8);
            buffer[offset + 1] = (byte)(transactionId & 0xFF);
        }

        /// Modbus TCP için sabit Protocol ID (0x0000) değerini yazar.
        private static void WriteProtocolId(byte[] buffer, int offset)
        {
            buffer[offset]     = 0x00;
            buffer[offset + 1] = 0x00;
        }

        /// MBAP Length alanını (bu alandan sonraki byte sayısı) Big-Endian olarak yazar.
        private static void WriteLength(byte[] buffer, int offset, ushort length)
        {
            buffer[offset]     = (byte)(length >> 8);
            buffer[offset + 1] = (byte)(length & 0xFF);
        }

        /// Herhangi bir 16-bit değeri Big-Endian olarak belirtilen offset'e yazar.
        private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset]     = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }
    }
}