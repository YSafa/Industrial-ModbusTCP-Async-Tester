using System;

namespace ModbusTester.Exceptions
{
    /// <summary>
    /// Bağlantı veya işlem belirlenen süre içinde tamamlanamadığında fırlatılır.
    /// </summary>
    public class ModbusTimeoutException : Exception
    {
        public ModbusTimeoutException(string message) : base(message) { }
    }
}