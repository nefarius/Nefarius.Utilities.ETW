using System.Runtime.CompilerServices;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW;

internal readonly struct EtwJsonWriter : IEtwWriter
{
    private readonly Utf8JsonWriter _writer;

    public EtwJsonWriter(Utf8JsonWriter writer)
    {
        this._writer = writer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void WriteEventBegin(EventMetadata metadata, RuntimeEventMetadata runtimeMetadata)
    {
        _writer.WriteStartObject();
        _writer.WritePropertyName("Event");
        _writer.WriteStartObject();

        _writer.WritePropertyName("Timestamp");
        _writer.WriteNumberValue(runtimeMetadata.Timestamp);

        _writer.WritePropertyName("ProviderGuid");
        _writer.WriteStringValue(metadata.ProviderGuid.ToString("D"));

        _writer.WritePropertyName("Id");
        _writer.WriteNumberValue(metadata.Id);

        _writer.WritePropertyName("Version");
        _writer.WriteNumberValue(metadata.Version);

        _writer.WritePropertyName("ProcessId");
        _writer.WriteNumberValue(runtimeMetadata.ProcessId);

        _writer.WritePropertyName("ThreadId");
        _writer.WriteNumberValue(runtimeMetadata.ThreadId);

        _writer.WritePropertyName("ProcessorNumber");
        _writer.WriteNumberValue(runtimeMetadata.ProcessorNumber);

        Guid activityId = runtimeMetadata.ActivityId;
        if (activityId != Guid.Empty)
        {
            _writer.WritePropertyName("ActivityId");
            _writer.WriteStringValue(activityId.ToString("D"));
        }

        Guid relatedActivityId = runtimeMetadata.RelatedActivityId;
        if (relatedActivityId != Guid.Empty)
        {
            _writer.WritePropertyName("RelatedActivityId");
            _writer.WriteStringValue(relatedActivityId.ToString("D"));
        }

        ulong[]? stacks = runtimeMetadata.GetStacks(out ulong matchId);
        if (matchId != 0)
        {
            _writer.WritePropertyName("StackMatchId");
            _writer.WriteNumberValue(matchId);
        }

        if (stacks != null)
        {
            _writer.WritePropertyName("Stacks");
            _writer.WriteStartArray();
            for (int i = 0; i < stacks.Length; ++i)
            {
                _writer.WriteNumberValue(stacks[i]);
            }

            _writer.WriteEndArray();
        }

        _writer.WritePropertyName("Name");
        _writer.WriteStringValue(metadata.Name);

        _writer.WritePropertyName("Properties");
        _writer.WriteStartArray();
        _writer.WriteStartObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteEventEnd()
    {
        _writer.WriteEndObject();
        _writer.WriteEndArray();
        _writer.WriteEndObject();
        _writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructBegin()
    {
        _writer.WriteStartObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructEnd()
    {
        _writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyBegin(PropertyMetadata metadata)
    {
        _writer.WritePropertyName(metadata.Name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyEnd()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayBegin()
    {
        _writer.WriteStartArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayEnd()
    {
        _writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiString(string value)
    {
        _writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeString(string value)
    {
        _writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt8(sbyte value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt8(byte value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value)
    {
        _writer.WriteBooleanValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBinary(byte[] value)
    {
        _writer.WriteBase64StringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid value)
    {
        _writer.WriteStringValue(value.ToString("D"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePointer(ulong value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFileTime(DateTime value)
    {
        _writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSystemTime(DateTime value)
    {
        _writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSid(string value)
    {
        _writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeChar(char value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiChar(char value)
    {
        _writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHexDump(byte[] value)
    {
        _writer.WriteBase64StringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteWbemSid(string value)
    {
        _writer.WriteStringValue(value);
    }
}