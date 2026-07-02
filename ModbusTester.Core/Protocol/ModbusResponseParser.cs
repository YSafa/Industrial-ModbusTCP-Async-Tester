using ModbusTester.Core.Exceptions;

namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Modbus TCP yanıt paketlerini doğrulayıp veri tiplerine çeviren statik sınıf.
    /// Okuma (FC01/02/03/04) ve yazma (FC05/06/16) yanıtlarını destekler.
    /// </summary>
    public static class ModbusResponseParser
    {
        // ---------------------------------------------------------
        // OKUMA YANIT AYRIŞTIRICILARI
        // ---------------------------------------------------------

        /// <summary>
        /// FC03/FC04 yanıtını çözümleyip doğrudan çağıranın verdiği hedef tampona (destination) yazar.
        /// Artık yeni bir ushort[] tahsis etmez; response de ReadOnlySpan olarak alınır.
        /// </summary>
        public static void ParseReadRegistersResponse(ReadOnlySpan<byte> response, Span<ushort> destination, ushort expectedTransactionId)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);

            byte byteCount = response[8];
            int registerCount = byteCount / 2;

            // Ek güvenlik: slave'in bildirdiği register sayısı, çağıranın ayırdığı tampon boyutuyla
            // uyuşmuyorsa (örn. bozuk/beklenmedik bir yanıt), Span sınırlarını aşmadan önce
            // burada net bir protokol hatası fırlatıyoruz. Bu kontrol öncesinde ModbusClient
            // tarafında yapılan chunkData.Length karşılaştırmasının artık burada olması, mantığı
            // tek bir yere topluyor.
            if (registerCount != destination.Length)
            {
                throw new ModbusProtocolException(0x04,
                    $"Yanıttaki register sayısı beklenenle uyuşmuyor. Beklenen: {destination.Length}, Gelen: {registerCount}.");
            }

            for (int i = 0; i < registerCount; i++)
            {
                int offset = 9 + (i * 2);
                destination[i] = (ushort)((response[offset] << 8) | response[offset + 1]);
            }
        }

        private static void ValidateTransactionId(ReadOnlySpan<byte> response, ushort expectedTransactionId)
        {
            ushort receivedId = (ushort)((response[0] << 8) | response[1]);

            if (receivedId != expectedTransactionId)
            {
                throw new ModbusProtocolException(0,
                    $"Transaction ID uyuşmuyor. Beklenen: {expectedTransactionId}, Gelen: {receivedId}.");
            }
        }

        private static void ValidateNotException(ReadOnlySpan<byte> response)
        {
            byte functionCode = response[7];

            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response[8];
                throw new ModbusProtocolException(exceptionCode, $"{exceptionCode:D2} - {GetExceptionDescription(exceptionCode)}");
            }
        }

        /// <summary>
        /// FC01 (Read Coils) ve FC02 (Read Discrete Inputs) yanıtlarını çözümler.
        /// Veriler byte içine sıkıştırılmış (bit-packed) halde gelir; LSB'den itibaren ayrıştırılır.
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

                // Sağa kaydırıp en düşük biti maskeleyerek (& 0x01) ilgili bitin durumunu okuyoruz.
                bits[i] = ((response[9 + byteIndex] >> bitIndex) & 0x01) == 1;
            }

            return bits;
        }

        // ---------------------------------------------------------
        // YAZMA YANIT DOĞRULAYICILARI
        // ---------------------------------------------------------

        /// <summary>
        /// FC05 (Write Single Coil) ve FC06 (Write Single Register) yanıtlarını doğrular.
        /// Modbus bu fonksiyonlara "echo" yanıtı döndürür: gönderilen paket aynen yansıtılır.
        ///
        /// Doğrulama kapsamı: Transaction ID + Exception kontrolü + Function Code + Adres eşleşmesi.
        /// </summary>
        /// <param name="response">Slave'den gelen ham yanıt paketi</param>
        /// <param name="expectedTransactionId">İstekte kullanılan Transaction ID</param>
        /// <param name="expectedFunctionCode">Gönderilen fonksiyon kodu (FC05 veya FC06)</param>
        /// <param name="expectedAddress">Yazma yapılan adres; echo'nun doğru adrese ait olduğunu teyit eder</param>
        public static void ValidateWriteSingleResponse(
            byte[] response, ushort expectedTransactionId,
            ModbusFunctionCode expectedFunctionCode, ushort expectedAddress)
        {
            ValidateTransactionId(response, expectedTransactionId);
            ValidateNotException(response);
            ValidateFunctionCode(response, expectedFunctionCode);

            // Echo yanıtındaki adresin bizim yazdığımız adresle eşleştiğini doğruluyoruz.
            ushort echoAddress = (ushort)((response[8] << 8) | response[9]);

            if (echoAddress != expectedAddress)
            {
                throw new ModbusProtocolException(0,
                    $"Yazma yanıtındaki adres uyuşmuyor. Beklenen: {expectedAddress}, Gelen: {echoAddress}.");
            }
        }

        /// <summary>
        /// FC16 (Write Multiple Registers) yanıtını doğrular.
        ///
        /// FC16 echo yanıtı daha kısadır (12 byte): slave yalnızca başlangıç adresini ve
        /// yazılan register sayısını döndürür (tüm değerleri yansıtmaz).
        /// Doğrulama: Transaction ID + Exception + Function Code + Adres + Register Sayısı.
        /// </summary>
        /// <param name="expectedRegisterCount">Yazma isteğinde gönderilen register sayısı</param>
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
                    $"FC16 yanıtındaki adres uyuşmuyor. Beklenen: {expectedAddress}, Gelen: {echoAddress}.");
            }

            // Slave'in kaç register yazdığını teyit eden quantity alanını da kontrol ediyoruz.
            ushort echoQuantity = (ushort)((response[10] << 8) | response[11]);

            if (echoQuantity != expectedRegisterCount)
            {
                throw new ModbusProtocolException(0,
                    $"FC16 yanıtındaki register sayısı uyuşmuyor. Beklenen: {expectedRegisterCount}, Gelen: {echoQuantity}.");
            }
        }

        // ---------------------------------------------------------
        // ORTAK DOĞRULAMA YARDIMCILARI
        // ---------------------------------------------------------

        private static void ValidateTransactionId(byte[] response, ushort expectedTransactionId)
        {
            ushort receivedId = (ushort)((response[0] << 8) | response[1]);

            if (receivedId != expectedTransactionId)
            {
                throw new ModbusProtocolException(0,
                    $"Transaction ID uyuşmuyor. Beklenen: {expectedTransactionId}, Gelen: {receivedId}.");
            }
        }

        private static void ValidateNotException(byte[] response)
        {
            byte functionCode = response[7];

            // Slave hata döndürdüğünde function code'un 7. biti (MSB) 1'e set edilir (örn: 0x03 -> 0x83).
            if ((functionCode & 0x80) != 0)
            {
                byte exceptionCode = response[8];
                throw new ModbusProtocolException(exceptionCode,
                    $"{exceptionCode:D2} - {GetExceptionDescription(exceptionCode)}");
            }
        }

        private static void ValidateFunctionCode(byte[] response, ModbusFunctionCode expected)
        {
            byte received = response[7];

            if (received != (byte)expected)
            {
                throw new ModbusProtocolException(0,
                    $"Function code uyuşmuyor. Beklenen: 0x{(byte)expected:X2}, Gelen: 0x{received:X2}.");
            }
        }

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
                _    => "Bilinmeyen Modbus hata kodu"
            };
        }
    }
}