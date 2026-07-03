using System;

namespace ModbusTester.Core.Exceptions
{
    /// <summary>
    /// Thrown when a connection or I/O operation does not complete within the configured timeout.
    /// </summary>
    public class ModbusTimeoutException : Exception
    {
        public ModbusTimeoutException(string message) : base(message) { }
    }
}