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
        public static float ToFloat(ushort[] registers, bool inverse)
        {
            ValidateLength(registers, 2, nameof(ToFloat));

            // inverse durumuna göre hangi register'ın yüksek/düşük kelime olduğunu belirliyoruz.
            ushort highWord = inverse ? registers[1] : registers[0];
            ushort lowWord = inverse ? registers[0] : registers[1];

            // 32-bit'lik birleşik değeri bit kaydırma ile oluşturuyoruz:
            // Yüksek kelimeyi 16 bit sola kaydırıp, düşük kelimeyle OR'luyoruz.
            // Bu işlem byte dizisi/Array.Reverse kullanmadan, doğrudan ve hatasız şekilde 32-bit değeri kurar.
            uint bits = ((uint)highWord << 16) | lowWord;

            // 32-bit'lik ham bit deseni, IEEE 754 standardına göre float olarak yorumlanır.
            return BitConverter.UInt32BitsToSingle(bits);
        }

        /// <summary>
        /// İki register'ı (32-bit) birleştirip işaretli (signed) Int32 değerine çevirir.
        /// Word sıralaması ToFloat ile birebir aynı mantığı kullanır.
        /// </summary>
        public static int ToLong(ushort[] registers, bool inverse)
        {
            ValidateLength(registers, 2, nameof(ToLong));

            ushort highWord = inverse ? registers[1] : registers[0];
            ushort lowWord = inverse ? registers[0] : registers[1];

            uint bits = ((uint)highWord << 16) | lowWord;

            // unchecked cast: uint'in üst bitini işaret biti olarak yorumlamak için doğrudan int'e çeviriyoruz.
            return unchecked((int)bits);
        }

        /// <summary>
        /// Dört register'ı (64-bit) birleştirip IEEE 754 double değerine çevirir.
        ///
        /// mbslave standardı (ABCD - Big Endian Word Order):
        ///   inverse=false -> register sırası: [W0(en yüksek), W1, W2, W3(en düşük)]
        ///   inverse=true  -> register sırası tamamen ters çevrilir: [W3(en yüksek), W2, W1, W0(en düşük)]
        /// </summary>
        public static double ToDouble(ushort[] registers, bool inverse)
        {
            ValidateLength(registers, 4, nameof(ToDouble));

            // inverse true ise word sırasını tamamen tersine çeviriyoruz (mbslave'in "Inverse" modu).
            ushort w0 = inverse ? registers[3] : registers[0]; // En anlamlı (most significant) kelime
            ushort w1 = inverse ? registers[2] : registers[1];
            ushort w2 = inverse ? registers[1] : registers[2];
            ushort w3 = inverse ? registers[0] : registers[3]; // En az anlamlı (least significant) kelime

            // 64-bit'lik birleşik değeri 4 adet 16-bit parçayı sırasıyla 48, 32, 16 ve 0 bit kaydırarak kuruyoruz.
            // Bu, her kelimenin 64-bit'lik değer içindeki doğru konuma (en anlamlıdan en az anlamlıya) yerleşmesini sağlar.
            ulong bits = ((ulong)w0 << 48) | ((ulong)w1 << 32) | ((ulong)w2 << 16) | w3;

            // 64-bit'lik ham bit deseni, IEEE 754 standardına göre double olarak yorumlanır.
            return BitConverter.UInt64BitsToDouble(bits);
        }

        /// <summary>
        /// Dönüştürme metotlarına gelen register dizisinin null olmadığını ve beklenen
        /// uzunlukta olduğunu doğrular; aksi halde anlamlı bir hata fırlatır.
        /// </summary>
        private static void ValidateLength(ushort[] registers, int expectedLength, string methodName)
        {
            if (registers == null)
            {
                throw new ArgumentNullException(nameof(registers), $"{methodName}: register dizisi null olamaz.");
            }

            if (registers.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"{methodName}: {expectedLength} adet register bekleniyordu, {registers.Length} adet geldi.",
                    nameof(registers));
            }
        }
    }
}