using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>
///     Reads encoded relay frames from the input stream, bounded before anything is allocated.
/// </summary>
internal static class FrameInput
{
    public static IAsyncEnumerable<byte[]> ReadAsync(Stream input, InputFormat format, int maxFrameBytes,
        CancellationToken cancellationToken) =>
        format == InputFormat.Binary
            ? ReadBinaryAsync(input, maxFrameBytes, cancellationToken)
            : ReadBase64NdjsonAsync(input, maxFrameBytes, cancellationToken);

    /// <summary>
    ///     Concatenated frames. The declared length of each frame is checked against the limit before its buffer exists.
    /// </summary>
    public static async IAsyncEnumerable<byte[]> ReadBinaryAsync(Stream input, int maxFrameBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RawEtwRelayLimits limits = new() { MaxFrameLength = maxFrameBytes };

        while (true)
        {
            byte[]? frame = await RawEtwFrameCodec.ReadFrameBytesAsync(input, limits, cancellationToken)
                .ConfigureAwait(false);
            if (frame is null)
            {
                yield break;
            }

            yield return frame;
        }
    }

    /// <summary>
    ///     One <c>{"frame":"&lt;base64&gt;"}</c> object per line. A line longer than the largest legal frame in Base64
    ///     is rejected while it is read, so a missing newline cannot grow memory without bound.
    /// </summary>
    public static async IAsyncEnumerable<byte[]> ReadBase64NdjsonAsync(Stream input, int maxFrameBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int maxLine = checked((maxFrameBytes + 2) / 3 * 4 + 64);
        byte[] line = new byte[Math.Min(maxLine, 4096)];
        byte[] chunk = new byte[16 * 1024];
        int length = 0;
        long lineNumber = 1;

        while (true)
        {
            int read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (int i = 0; i < read; i++)
            {
                byte b = chunk[i];
                if (b == (byte)'\n')
                {
                    byte[]? frame = ParseLine(line, length, lineNumber, maxFrameBytes);
                    length = 0;
                    lineNumber++;
                    if (frame is not null)
                    {
                        yield return frame;
                    }

                    continue;
                }

                if (length == line.Length)
                {
                    if (length >= maxLine)
                    {
                        throw new InputException(
                            $"Line {lineNumber} is longer than {maxLine} bytes, the largest a {maxFrameBytes}-byte " +
                            "frame can take; refusing to buffer it.");
                    }

                    Array.Resize(ref line, Math.Min(maxLine, line.Length * 2));
                }

                line[length++] = b;
            }
        }

        if (length > 0)
        {
            byte[]? frame = ParseLine(line, length, lineNumber, maxFrameBytes);
            if (frame is not null)
            {
                yield return frame;
            }
        }
    }

    private static byte[]? ParseLine(byte[] buffer, int length, long lineNumber, int maxFrameBytes)
    {
        int start = 0;
        int end = length;

        // Windows PowerShell and some editors prefix text with a UTF-8 byte order mark.
        if (lineNumber == 1 && length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            start = 3;
        }

        while (start < end && buffer[start] is (byte)' ' or (byte)'\t' or (byte)'\r')
        {
            start++;
        }

        while (end > start && buffer[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r')
        {
            end--;
        }

        if (start == end)
        {
            return null;
        }

        string text;
        try
        {
            using JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, start, end - start),
                new JsonDocumentOptions { MaxDepth = 4 });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InputException($"Line {lineNumber} is not a JSON object.");
            }

            string? frame = null;
            int properties = 0;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                properties++;
                if (property.Name == "frame" && property.Value.ValueKind == JsonValueKind.String)
                {
                    frame = property.Value.GetString();
                }
            }

            if (properties != 1 || frame is null)
            {
                throw new InputException($"Line {lineNumber} must be exactly {{\"frame\":\"<base64>\"}}.");
            }

            text = frame;
        }
        catch (JsonException ex)
        {
            throw new InputException($"Line {lineNumber} is not valid JSON: {ex.Message}", ex);
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(text);
        }
        catch (FormatException ex)
        {
            throw new InputException($"Line {lineNumber} does not hold valid Base64.", ex);
        }

        if (decoded.Length > maxFrameBytes)
        {
            throw new InputException($"Line {lineNumber} holds a {decoded.Length}-byte frame; the limit is {maxFrameBytes}.");
        }

        return decoded;
    }
}
