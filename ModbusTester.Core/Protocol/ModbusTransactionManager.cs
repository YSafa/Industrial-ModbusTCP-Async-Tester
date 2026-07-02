namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Her Modbus isteği için benzersiz ve sürekli artan bir Transaction ID üretir.
    /// Transaction ID, gelen yanıtın hangi isteğe ait olduğunu eşleştirmek için kullanılır.
    /// Birden fazla thread aynı anda istek gönderebileceğinden, ID üretimi thread-safe olmalıdır.
    /// </summary>
    public class ModbusTransactionManager
    {
        // Interlocked ile atomik artırım yapacağımız için int olarak tutuyoruz;
        // ushort'a son adımda dönüştürülecek (Modbus TransactionID alanı 2 byte'tır).
        private int _currentTransactionId;

        /// <summary>
        /// Bir sonraki kullanılabilir Transaction ID'yi thread-safe şekilde üretir ve döndürür.
        /// 65535 değerine ulaşıldığında 0'a sarılır (wrap-around), çünkü alan sadece 2 byte (0-65535).
        /// </summary>
        public ushort GetNextTransactionId()
        {
            // Interlocked.Increment, birden fazla thread aynı anda çağırsa bile
            // değerin atomik (kesintisiz) şekilde artmasını garanti eder.
            int next = Interlocked.Increment(ref _currentTransactionId);

            // (ushort) cast'i zaten alt 16 biti alıp üst bitleri düşürerek taşma durumunda
            // otomatik başa sarmayı garanti eder; modulo işlemine gerek yok.
            return (ushort)next;
        }
    }
}