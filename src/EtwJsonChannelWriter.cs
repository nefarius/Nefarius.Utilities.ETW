using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     Carries a single decoded ETW event as a pooled UTF-8 JSON byte buffer.
/// </summary>
internal readonly struct PooledEventBuffer
{
    internal readonly byte[] Rented;
    internal readonly int Length;

    internal PooledEventBuffer(byte[] rented, int length)
    {
        Rented = rented;
        Length = length;
    }

    internal ReadOnlyMemory<byte> Memory => new(Rented, 0, Length);
}

/// <summary>
///     An <see cref="IEtwWriter" /> implementation that serializes each ETW event into a pooled
///     UTF-8 JSON buffer and writes the completed buffer into a bounded channel.
/// </summary>
internal sealed class EtwJsonChannelWriter : IEtwWriter
{
    private readonly ArrayBufferWriter<byte> _bufferWriter = new(initialCapacity: 4096);
    private readonly ChannelWriter<PooledEventBuffer> _channelWriter;
    private readonly CancellationToken _cancellationToken;
    private Utf8JsonWriter _jsonWriter;

    internal EtwJsonChannelWriter(ChannelWriter<PooledEventBuffer> channelWriter,
        CancellationToken cancellationToken = default,
        JsonWriterOptions writerOptions = default)
    {
        _channelWriter = channelWriter;
        _cancellationToken = cancellationToken;
        _jsonWriter = new Utf8JsonWriter(_bufferWriter, writerOptions);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void WriteEventBegin(EventMetadata metadata, RuntimeEventMetadata runtimeMetadata)
    {
        _bufferWriter.Clear();
        _jsonWriter.Reset(_bufferWriter);

        _jsonWriter.WriteStartObject();
        _jsonWriter.WritePropertyName("Event");
        _jsonWriter.WriteStartObject();

        _jsonWriter.WritePropertyName("Timestamp");
        _jsonWriter.WriteNumberValue(runtimeMetadata.Timestamp);

        _jsonWriter.WritePropertyName("ProviderGuid");
        _jsonWriter.WriteStringValue(metadata.ProviderGuid.ToString("D"));

        _jsonWriter.WritePropertyName("Id");
        _jsonWriter.WriteNumberValue(metadata.Id);

        _jsonWriter.WritePropertyName("Version");
        _jsonWriter.WriteNumberValue(metadata.Version);

        _jsonWriter.WritePropertyName("ProcessId");
        _jsonWriter.WriteNumberValue(runtimeMetadata.ProcessId);

        _jsonWriter.WritePropertyName("ThreadId");
        _jsonWriter.WriteNumberValue(runtimeMetadata.ThreadId);

        _jsonWriter.WritePropertyName("ProcessorNumber");
        _jsonWriter.WriteNumberValue(runtimeMetadata.ProcessorNumber);

        Guid activityId = runtimeMetadata.ActivityId;
        if (activityId != Guid.Empty)
        {
            _jsonWriter.WritePropertyName("ActivityId");
            _jsonWriter.WriteStringValue(activityId.ToString("D"));
        }

        Guid relatedActivityId = runtimeMetadata.RelatedActivityId;
        if (relatedActivityId != Guid.Empty)
        {
            _jsonWriter.WritePropertyName("RelatedActivityId");
            _jsonWriter.WriteStringValue(relatedActivityId.ToString("D"));
        }

        ulong[]? stacks = runtimeMetadata.GetStacks(out ulong matchId);
        if (matchId != 0)
        {
            _jsonWriter.WritePropertyName("StackMatchId");
            _jsonWriter.WriteNumberValue(matchId);
        }

        if (stacks != null)
        {
            _jsonWriter.WritePropertyName("Stacks");
            _jsonWriter.WriteStartArray();
            for (int i = 0; i < stacks.Length; ++i)
            {
                _jsonWriter.WriteNumberValue(stacks[i]);
            }

            _jsonWriter.WriteEndArray();
        }

        _jsonWriter.WritePropertyName("Name");
        _jsonWriter.WriteStringValue(metadata.Name);

        _jsonWriter.WritePropertyName("Properties");
        _jsonWriter.WriteStartArray();
        _jsonWriter.WriteStartObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteEventEnd()
    {
        _jsonWriter.WriteEndObject();
        _jsonWriter.WriteEndArray();
        _jsonWriter.WriteEndObject();
        _jsonWriter.WriteEndObject();
        _jsonWriter.Flush();

        int length = _bufferWriter.WrittenCount;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        _bufferWriter.WrittenSpan.CopyTo(rented);

        // Block the worker thread when the channel is full, applying backpressure.
        // Pass the cancellation token so that a cancelled consumer unblocks this call
        // rather than causing an infinite wait when the bounded channel is saturated.
        _channelWriter.WriteAsync(new PooledEventBuffer(rented, length), _cancellationToken)
            .AsTask().GetAwaiter().GetResult();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructBegin() => _jsonWriter.WriteStartObject();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructEnd() => _jsonWriter.WriteEndObject();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyBegin(PropertyMetadata metadata) => _jsonWriter.WritePropertyName(metadata.Name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyEnd() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayBegin() => _jsonWriter.WriteStartArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayEnd() => _jsonWriter.WriteEndArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiString(string value) => _jsonWriter.WriteStringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeString(string value) => _jsonWriter.WriteStringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt8(sbyte value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt8(byte value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value) => _jsonWriter.WriteBooleanValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBinary(byte[] value) => _jsonWriter.WriteBase64StringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid value) => _jsonWriter.WriteStringValue(value.ToString("D"));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePointer(ulong value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFileTime(DateTime value) => _jsonWriter.WriteStringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSystemTime(DateTime value) => _jsonWriter.WriteStringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSid(string value) => _jsonWriter.WriteStringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeChar(char value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiChar(char value) => _jsonWriter.WriteNumberValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHexDump(byte[] value) => _jsonWriter.WriteBase64StringValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteWbemSid(string value) => _jsonWriter.WriteStringValue(value);
}
