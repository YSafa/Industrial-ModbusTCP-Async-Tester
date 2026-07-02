namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Ham ushort register değerlerini anlamlı veri tiplerine (short, float, long, double, hex, binary)
    /// dönüştüren statik yardımcı sınıf.
    ///
    /// ÖNEMLİ DÜZELTME NOTU:
    /// Önceki sürümde byte[] dizisi oluşturup Array.Reverse + BitConverter zinciriyle dönüşüm yapılıyordu.
    /// Bu yöntem teorik olarak doğru olsa da, byte dizisi ters çevirme adımları insan gözüyle takip etmesi
    /// zor ve hataya çok açık bir yaklaşımdır (mbslave testinde Double değeri 5.3E-315 gibi anlamsız bir
    /// sonuç üretmişti). Bu yüzden tamamen BİT KAYDIRMA (bit shifting) tabanlı, byte dizisi veya
    /// Array.Reverse hiç kullanmayan, çok daha az hataya açık ve doğrulanması kolay bir yönteme geçildi.
    /// Register sırası artık doğrudan matematiksel kaydırmalarla (<<, |) kontrol ediliyor.
    /// </summary>
    public static class ModbusDataConverter
    {
        /// <summary>
        /// Tek bir register'ı işaretli (signed) 16-bit tam sayıya çevirir.
        /// </summary>
        public static short ToSigned(ushort value)
        {
            // unchecked: 32768-65535 aralığındaki değerler negatif short'a dönüşürken taşma kontrolü yapılmasın diye.
            return unchecked((short)value);
        }

        /// <summary>
        /// Register değerini "0x00A1" formatında okunabilir hex string'e çevirir.
        /// </summary>
        public static string ToHex(ushort value)
        {
            return "0x" + value.ToString("X4");
        }

        /// <summary>
        /// Register değerini 16 haneli, MSB solda olacak şekilde ikilik (binary) string'e çevirir.
        /// </summary>
        public static string ToBinary(ushort value)
        {
            return Convert.ToString(value, 2).PadLeft(16, '0');
        }

        /// <summary>
        /// İki register'ı (32-bit) birleştirip IEEE 754 float değerine çevirir.
        ///
        /// mbslave standardı (ABCD - Big Endian Word Order):
        ///   inverse=false -> registers[0] YÜKSEK kelime (high word), registers[1] DÜŞÜK kelime (low word)
        ///   inverse=true  -> registers[0] DÜŞÜK kelime, registers[1] YÜKSEK kelime (word swap / CDAB)
        /// </summary>
        public static float ToFloat(ReadOnlySpan<ushort> registers, bool inverse)
        {
            ValidateLength(registers, 2, nameof(ToFloat));

            ushort highWord = inverse ? registers[1] : registers[0];
            ushort lowWord = inverse ? registers[0] : registers[1];

            uint bits = ((uint)highWord << 16) | lowWord;
            return BitConverter.UInt32BitsToSingle(bits);
        }

        /// <summary>
        /// İki register'ı (32-bit) birleştirip işaretli (signed) Int32 değerine çevirir.
        /// Word sıralaması ToFloat ile birebir aynı mantığı kullanır.
        /// </summary>
        public static int ToLong(ReadOnlySpan<ushort> registers, bool inverse)
        {
            ValidateLength(registers, 2, nameof(ToLong));

            ushort highWord = inverse ? registers[1] : registers[0];
            ushort lowWord = inverse ? registers[0] : registers[1];

            uint bits = ((uint)highWord << 16) | lowWord;
            return unchecked((int)bits);
        }

        /// <summary>
        /// Dört register'ı (64-bit) birleştirip IEEE 754 double değerine çevirir.
        ///
        /// mbslave standardı (ABCD - Big Endian Word Order):
        ///   inverse=false -> register sırası: [W0(en yüksek), W1, W2, W3(en düşük)]
        ///   inverse=true  -> register sırası tamamen ters çevrilir: [W3(en yüksek), W2, W1, W0(en düşük)]
        /// </summary>
        public static double ToDouble(ReadOnlySpan<ushort> registers, bool inverse)
        {
            ValidateLength(registers, 4, nameof(ToDouble));

            ushort w0 = inverse ? registers[3] : registers[0];
            ushort w1 = inverse ? registers[2] : registers[1];
            ushort w2 = inverse ? registers[1] : registers[2];
            ushort w3 = inverse ? registers[0] : registers[3];

            ulong bits = ((ulong)w0 << 48) | ((ulong)w1 << 32) | ((ulong)w2 << 16) | w3;
            return BitConverter.UInt64BitsToDouble(bits);
        }

        /// <summary>
        /// ReadOnlySpan bir struct olduğu için null olamaz; null bir diziden dönüştürülmüş span
        /// otomatik olarak Length=0 üretir ve bu zaten aşağıdaki kontrole takılır — ayrı bir null
        /// kontrolüne gerek yok.
        /// </summary>
        private static void ValidateLength(ReadOnlySpan<ushort> registers, int expectedLength, string methodName)
        {
            if (registers.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"{methodName}: {expectedLength} adet register bekleniyordu, {registers.Length} adet geldi.",
                    nameof(registers));
            }
        }
    }
}