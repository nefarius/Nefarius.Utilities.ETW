using System.Collections.Concurrent;

namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

/// <summary>
///     Turns the value of a TraceLogging field that uses custom serialization into something presentable.
/// </summary>
/// <param name="typeInfo">The opaque type-info bytes the producer attached to the field.</param>
/// <param name="value">
///     The value read using the field's base in-type (for example <c>byte[]</c> for a binary field).
/// </param>
/// <returns>
///     <see langword="null" />, a <see cref="string" />, <c>byte[]</c>, <see cref="bool" />, a primitive number, a
///     <see cref="Guid" /> or a <see cref="DateTime" />.
/// </returns>
public delegate object? TraceLoggingCustomDecoder(ReadOnlySpan<byte> typeInfo, object? value);

/// <summary>
///     Optional registry of decoders for TraceLogging custom-serialized fields, keyed by their exact type-info bytes.
///     Without a registered decoder the field is emitted using its base in-type (raw bytes for binary fields); the
///     type info is opaque to this library.
/// </summary>
public static class TraceLoggingCustomDecoders
{
    private static readonly ConcurrentDictionary<string, TraceLoggingCustomDecoder> Decoders = new();

    /// <summary>Registers (or replaces) the decoder for fields carrying exactly <paramref name="typeInfo" />.</summary>
    public static void Register(ReadOnlySpan<byte> typeInfo, TraceLoggingCustomDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        Decoders[Convert.ToHexString(typeInfo)] = decoder;
    }

    /// <summary>Removes the decoder registered for <paramref name="typeInfo" />.</summary>
    public static bool Unregister(ReadOnlySpan<byte> typeInfo)
    {
        return Decoders.TryRemove(Convert.ToHexString(typeInfo), out _);
    }

    internal static bool TryGet(byte[] typeInfo, out TraceLoggingCustomDecoder? decoder)
    {
        return Decoders.TryGetValue(Convert.ToHexString(typeInfo), out decoder);
    }
}