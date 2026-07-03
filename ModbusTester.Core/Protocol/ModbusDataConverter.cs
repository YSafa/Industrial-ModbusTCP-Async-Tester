using System;

namespace ModbusTester.Core.Protocol
{
    /// <summary>
    /// Static helper class that converts raw ushort register values into meaningful data types
    /// (short, float, long, double, hex, binary). Modbus operates in Big-Endian over the wire,
    /// while .NET (including BitConverter) assumes Little-Endian; every multi-byte conversion
    /// below is therefore explicit about byte order.
    /// </summary>
    public static class ModbusDataConverter
    {
        /// <summary>
        /// Converts a single register to a signed 16-bit integer.
        /// </summary>
        public static short ToSigned(ushort value)
        {
            // unchecked: values in the 32768-65535 range should convert to a negative short
            // without triggering an overflow check.
            return unchecked((short)value);
        }

        /// <summary>
        /// Converts a register value to a readable "0x00A1"-style hex string.
        /// </summary>
        public static string ToHex(ushort value)
        {
            return "0x" + value.ToString("X4");
        }

        /// <summary>
        /// Converts a register value to a 16-character binary string, MSB first.
        /// </summary>
        public static string ToBinary(ushort value)
        {
            return Convert.ToString(value, 2).PadLeft(16, '0');
        }

        /// <summary>
        /// Combines two registers (32-bit) into an IEEE 754 float.
        ///
        /// mbslave convention (ABCD - Big-Endian word order):
        ///   inverse=false -> registers[0] = high word, registers[1] = low word
        ///   inverse=true  -> registers[0] = low word, registers[1] = high word (word swap / CDAB)
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
        /// Combines two registers (32-bit) into a signed Int32.
        /// Word order follows the same convention as ToFloat.
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
        /// Combines four registers (64-bit) into an IEEE 754 double.
        ///
        /// mbslave convention (ABCD - Big-Endian word order):
        ///   inverse=false -> word order: [W0(most significant), W1, W2, W3(least significant)]
        ///   inverse=true  -> word order fully reversed: [W3(most significant), W2, W1, W0(least significant)]
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
        /// ReadOnlySpan is a struct and cannot be null; a span produced from a null array
        /// automatically has Length=0, which is already caught by the length check below —
        /// no separate null check is needed.
        /// </summary>
        private static void ValidateLength(ReadOnlySpan<ushort> registers, int expectedLength, string methodName)
        {
            if (registers.Length != expectedLength)
            {
                throw new ArgumentException(
                    $"{methodName}: expected {expectedLength} register(s), received {registers.Length}.",
                    nameof(registers));
            }
        }
    }
}