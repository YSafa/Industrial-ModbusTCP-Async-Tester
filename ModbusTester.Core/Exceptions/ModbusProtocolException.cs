namespace ModbusTester.Core.Exceptions
{
    /// <summary>
    /// Slave cihazın bir Modbus exception yanıtı (function code | 0x80) döndürmesi durumunda fırlatılır.
    /// İçinde standart Modbus exception kodu ve buna karşılık gelen açıklama bulunur.
    /// </summary>
    public class ModbusProtocolException : Exception
    {
        // Slave'in döndürdüğü ham exception kodu (1, 2, 3, 4 vb.)
        public byte ExceptionCode { get; }

        public ModbusProtocolException(byte exceptionCode, string message) : base(message)
        {
            ExceptionCode = exceptionCode;
        }
    }
}