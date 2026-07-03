using System;

namespace ModbusTester.Core.Exceptions
{
    /// <summary>
    /// Thrown when the slave device returns a legitimate Modbus exception response
    /// (function code | 0x80). Contains the raw exception code and its description.
    /// </summary>
    public class ModbusProtocolException : Exception
    {
        // Raw exception code returned by the slave (1, 2, 3, 4, etc.)
        public byte ExceptionCode { get; }

        public ModbusProtocolException(byte exceptionCode, string message) : base(message)
        {
            ExceptionCode = exceptionCode;
        }
    }
}