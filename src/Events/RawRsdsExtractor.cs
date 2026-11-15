using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.CustomParsers;
using Nefarius.Utilities.ETW.Relay;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     Extracts PDB identities from relayed <see cref="RawEtwRecord" /> payloads without TDH or native pointers.
/// </summary>
/// <remarks>
///     Supports <c>KernelTraceControl/ImageID/DbgID_RSDS</c> (opcode 36) and the legacy
///     <c>MSNT_SystemTrace/EventTrace/DbgIdRSDS</c> (opcode 64). Malformed records are reported as
///     <see langword="false" /> and never throw. The relayed data is untrusted: only the leaf file name is kept
///     and it must be a plain printable-ASCII name.
/// </remarks>
public static class RawRsdsExtractor
{
    private const byte ImageIdDbgIdRsdsOpcode = 36;

    /// <summary>
    ///     Attempts to extract a PDB identity from <paramref name="record" />.
    /// </summary>
    /// <returns><see langword="true" /> when the record is a well-formed RSDS event.</returns>
    public static bool TryExtract(RawEtwRecord? record, out PdbMetaData pdb)
    {
        pdb = default;

        if (record?.Payload is not { } payload)
        {
            return false;
        }

        int offset;
        if (record.ProviderId == CustomParserGuids.KernelTraceControlImageIdGuid &&
            record.Opcode == ImageIdDbgIdRsdsOpcode)
        {
            // ImageBase (u64), ProcessId (u32), then GUID, age and name.
            offset = sizeof(ulong) + sizeof(uint);
        }
        else if (record.ProviderId == CustomParserGuids.EventTraceGuid &&
                 record.Opcode == EventTraceParsers.DbgIdRsdsOpcode)
        {
            offset = 0;
        }
        else
        {
            return false;
        }

        // GUID + age + at least one name byte + terminator.
        if (payload.Length < offset + 16 + sizeof(uint) + 2)
        {
            return false;
        }

        Guid guid = new(payload.AsSpan(offset, 16));
        uint age = BitConverter.IsLittleEndian
            ? BitConverter.ToUInt32(payload, offset + 16)
            : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 16, 4));
        if (age > int.MaxValue)
        {
            return false;
        }

        ReadOnlySpan<byte> nameArea = payload.AsSpan(offset + 16 + sizeof(uint));
        int terminator = nameArea.IndexOf((byte)0);
        if (terminator <= 0)
        {
            return false;
        }

        ReadOnlySpan<byte> nameBytes = nameArea[..terminator];
        foreach (byte b in nameBytes)
        {
            if (b is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        string name = System.Text.Encoding.ASCII.GetString(nameBytes);
        string leaf = PathHelper.GetFileName(name);
        if (!IsSafeLeaf(leaf))
        {
            return false;
        }

        pdb = new PdbMetaData { PdbName = leaf, Guid = guid, Age = (int)age };
        return true;
    }

    private static bool IsSafeLeaf(string leaf)
    {
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or ".." || leaf.Length > 255)
        {
            return false;
        }

        return leaf.IndexOfAny(['<', '>', ':', '"', '|', '?', '*', '/', '\\', '#', '%']) < 0;
    }
}
