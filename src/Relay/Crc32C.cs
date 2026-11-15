namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     CRC-32C (Castagnoli), the checksum the relay frame format uses.
/// </summary>
internal static class Crc32C
{
    private const uint ReflectedPolynomial = 0x82F63B78;

    private static readonly uint[] Table = BuildTable();

    /// <summary>
    ///     Continues a checksum. Pass 0 for the first segment.
    /// </summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint value = ~crc;
        foreach (byte b in data)
        {
            value = Table[(value ^ b) & 0xFF] ^ (value >> 8);
        }

        return ~value;
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ ReflectedPolynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
