using System.Runtime.CompilerServices;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW;

public struct EtwJsonWriter : IEtwWriter
{
    private readonly Utf8JsonWriter writer;

    public EtwJsonWriter(Utf8JsonWriter writer)
    {
        this.writer = writer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void WriteEventBegin(EventMetadata metadata, RuntimeEventMetadata runtimeMetadata)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Event");
        writer.WriteStartObject();

        writer.WritePropertyName("Timestamp");
        writer.WriteNumberValue(runtimeMetadata.Timestamp);

        writer.WritePropertyName("ProviderGuid");
        writer.WriteStringValue(metadata.ProviderGuid.ToString("D"));

        writer.WritePropertyName("Id");
        writer.WriteNumberValue(metadata.Id);

        writer.WritePropertyName("Version");
        writer.WriteNumberValue(metadata.Version);

        writer.WritePropertyName("ProcessId");
        writer.WriteNumberValue(runtimeMetadata.ProcessId);

        writer.WritePropertyName("ThreadId");
        writer.WriteNumberValue(runtimeMetadata.ThreadId);

        writer.WritePropertyName("ProcessorNumber");
        writer.WriteNumberValue(runtimeMetadata.ProcessorNumber);

        Guid activityId = runtimeMetadata.ActivityId;
        if (activityId != Guid.Empty)
        {
            writer.WritePropertyName("ActivityId");
            writer.WriteStringValue(activityId.ToString("D"));
        }

        Guid relatedActivityId = runtimeMetadata.RelatedActivityId;
        if (relatedActivityId != Guid.Empty)
        {
            writer.WritePropertyName("RelatedActivityId");
            writer.WriteStringValue(relatedActivityId.ToString("D"));
        }

        ulong[]? stacks = runtimeMetadata.GetStacks(out ulong matchId);
        if (matchId != 0)
        {
            writer.WritePropertyName("StackMatchId");
            writer.WriteNumberValue(matchId);
        }

        if (stacks != null)
        {
            writer.WritePropertyName("Stacks");
            writer.WriteStartArray();
            for (int i = 0; i < stacks.Length; ++i)
            {
                writer.WriteNumberValue(stacks[i]);
            }

            writer.WriteEndArray();
        }

        writer.WritePropertyName("Name");
        writer.WriteStringValue(metadata.Name);

        writer.WritePropertyName("Properties");
        writer.WriteStartArray();
        writer.WriteStartObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteEventEnd()
    {
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructBegin()
    {
        writer.WriteStartObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructEnd()
    {
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyBegin(PropertyMetadata metadata)
    {
        writer.WritePropertyName(metadata.Name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePropertyEnd()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayBegin()
    {
        writer.WriteStartArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteArrayEnd()
    {
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiString(string value)
    {
        writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeString(string value)
    {
        writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt8(sbyte value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt8(byte value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value)
    {
        writer.WriteBooleanValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBinary(byte[] value)
    {
        writer.WriteBase64StringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid value)
    {
        writer.WriteStringValue(value.ToString("D"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePointer(ulong value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFileTime(DateTime value)
    {
        writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSystemTime(DateTime value)
    {
        writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSid(string value)
    {
        writer.WriteStringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnicodeChar(char value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAnsiChar(char value)
    {
        writer.WriteNumberValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHexDump(byte[] value)
    {
        writer.WriteBase64StringValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteWbemSid(string value)
    {
        writer.WriteStringValue(value);
    }
}