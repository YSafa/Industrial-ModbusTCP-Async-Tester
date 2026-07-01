using System;

namespace ModbusTester.Exceptions
{
    /// <summary>
    /// TCP soket bağlantısı kurulurken veya kesilirken oluşan hatalar için kullanılır.
    /// </summary>
    public class ModbusConnectionException : Exception
    {
        public ModbusConnectionException(string message) : base(message) { }

        public ModbusConnectionException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}