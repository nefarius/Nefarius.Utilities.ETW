namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Decoder for the plain LZ77 variant of Microsoft's Xpress compression ([MS-XCA] section 2.3), used for the
///     buffers of ETL files that were recorded with <c>EVENT_TRACE_COMPRESSED_MODE</c>.
/// </summary>
internal static class XpressLz77
{
    /// <summary>
    ///     Decompresses <paramref name="input" /> into <paramref name="output" />.
    /// </summary>
    /// <param name="input">The compressed bytes.</param>
    /// <param name="output">Receives the data; its length is the upper bound of the decompressed size.</param>
    /// <param name="written">Number of bytes written to <paramref name="output" />.</param>
    /// <returns>False when the input is malformed or does not fit into <paramref name="output" />.</returns>
    public static bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> output, out int written)
    {
        written = 0;
        int inPos = 0;
        int outPos = 0;
        int nibbleIndex = -1;
        uint flags = 0;
        int flagCount = 0;

        while (inPos < input.Length)
        {
            written = outPos;
            if (flagCount == 0)
            {
                if (inPos + 4 > input.Length)
                {
                    return false;
                }

                flags = (uint)(input[inPos] | input[inPos + 1] << 8 | input[inPos + 2] << 16 | input[inPos + 3] << 24);
                inPos += 4;
                flagCount = 32;
                if (inPos >= input.Length)
                {
                    break;
                }
            }

            bool isMatch = (flags & 0x80000000) != 0;
            flags <<= 1;
            flagCount--;

            if (!isMatch)
            {
                if (outPos >= output.Length)
                {
                    return false;
                }

                output[outPos++] = input[inPos++];
                continue;
            }

            if (inPos + 2 > input.Length)
            {
                return false;
            }

            int match = input[inPos] | input[inPos + 1] << 8;
            inPos += 2;
            int offset = (match >> 3) + 1;
            long length = match & 7;

            if (length == 7)
            {
                if (nibbleIndex < 0)
                {
                    if (inPos >= input.Length)
                    {
                        return false;
                    }

                    nibbleIndex = inPos;
                    length = input[inPos] & 0xF;
                    inPos++;
                }
                else
                {
                    length = input[nibbleIndex] >> 4;
                    nibbleIndex = -1;
                }

                if (length == 15)
                {
                    if (inPos >= input.Length)
                    {
                        return false;
                    }

                    length = input[inPos++];
                    if (length == 255)
                    {
                        if (inPos + 2 > input.Length)
                        {
                            return false;
                        }

                        length = input[inPos] | input[inPos + 1] << 8;
                        inPos += 2;
                        if (length == 0)
                        {
                            if (inPos + 4 > input.Length)
                            {
                                return false;
                            }

                            length = (uint)(input[inPos] | input[inPos + 1] << 8 | input[inPos + 2] << 16 |
                                            input[inPos + 3] << 24);
                            inPos += 4;
                        }

                        length -= 15 + 7;
                    }

                    length += 15;
                }

                length += 7;
            }

            length += 3;

            if (offset > outPos || length < 0 || length > output.Length - outPos)
            {
                return false;
            }

            // The source and destination may overlap; copy forward byte by byte.
            for (long i = 0; i < length; i++)
            {
                output[outPos] = output[outPos - offset];
                outPos++;
            }
        }

        written = outPos;
        return true;
    }
}
