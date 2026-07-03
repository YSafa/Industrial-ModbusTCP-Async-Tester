using System.Threading;

namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Generates a unique, monotonically increasing Transaction ID for each Modbus request.
    /// The Transaction ID is used to match an incoming response to its originating request.
    /// Since multiple threads may issue requests concurrently, ID generation must be thread-safe.
    /// </summary>
    public class ModbusTransactionManager
    {
        private int _currentTransactionId;

        /// <summary>
        /// Thread-safely produces the next available Transaction ID.
        /// The (ushort) cast naturally drops the upper bits, wrapping back to 0 once the
        /// 2-byte (0-65535) range is exceeded; no explicit modulo is needed.
        /// </summary>
        public ushort GetNextTransactionId()
        {
            int next = Interlocked.Increment(ref _currentTransactionId);
            return (ushort)next;
        }
    }
}