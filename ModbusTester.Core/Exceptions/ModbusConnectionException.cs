using System;

namespace ModbusTester.Core.Exceptions
{
    /// <summary>
    /// Thrown when establishing or closing a TCP socket connection fails.
    /// </summary>
    public class ModbusConnectionException : Exception
    {
        public ModbusConnectionException(string message) : base(message) { }

        public ModbusConnectionException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}