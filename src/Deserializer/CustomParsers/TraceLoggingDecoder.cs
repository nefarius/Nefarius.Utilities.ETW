using System.Text;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;

namespace Nefarius.Utilities.ETW.Deserializer;

/// <summary>
///     Decodes TraceLogging events from their self-describing event schema (extended data type 11), so that no TDH
///     is needed to learn field names and types.
/// </summary>
internal static unsafe class TraceLoggingDecoder
{
    private const ushort ExtTypeSchemaTl = 11;

    private const byte InTypeMask = 0x1F;
    private const byte InTypeChain = 0x80;
    private const byte InTypeVariableCount = 0x40;
    private const byte InTypeConstantCount = 0x20;
    private const byte OutTypeChain = 0x80;
    private const byte OutTypeMask = 0x7F;

    private const byte OutTypeBoolean = 3;
    private const byte OutTypeUtf8 = 35;
    private const int MaxSchemaDepth = 32;
    private const int MaxNodes = 4096;

    private enum InType : byte
    {
        Null = 0,
        UnicodeString = 1,
        AnsiString = 2,
        Int8 = 3,
        UInt8 = 4,
        Int16 = 5,
        UInt16 = 6,
        Int32 = 7,
        UInt32 = 8,
        Int64 = 9,
        UInt64 = 10,
        Float = 11,
        Double = 12,
        Bool32 = 13,
        Binary = 14,
        Guid = 15,
        Pointer = 16,
        FileTime = 17,
        SystemTime = 18,
        Sid = 19,
        HexInt32 = 20,
        HexInt64 = 21,
        CountedString = 22,
        CountedAnsiString = 23,
        Struct = 24,
        CountedBinary = 25
    }

    private sealed class Field
    {
        public string Name = string.Empty;
        public InType Type;
        public byte OutType;
        public bool IsArray;
        public bool IsVariableArray;
        public byte[]? TypeInfo;
        public ushort ConstantCount;
        public List<Field> Children = [];
        public PropertyMetadata Metadata = null!;
    }

    private sealed class Node(Field field)
    {
        public Field Field { get; } = field;

        public object? Value { get; set; }

        public List<Node>? Members { get; set; }

        public List<object?>? Elements { get; set; }
    }

    /// <summary>
    ///     Writes the event when it carries a valid TraceLogging schema; otherwise nothing is written.
    /// </summary>
    public static bool TryDecode<T>(EVENT_RECORD* record, EventRecordReader reader, T writer,
        RuntimeEventMetadata runtimeMetadata) where T : IEtwWriter
    {
        byte* schema = null;
        int schemaSize = 0;
        for (int i = 0; i < record->ExtendedDataCount; i++)
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM* item = record->ExtendedData + i;
            if (item->ExtType == ExtTypeSchemaTl)
            {
                schema = (byte*)item->DataPtr;
                schemaSize = item->DataSize;
                break;
            }
        }

        if (schema is null)
        {
            return false;
        }

        List<Node> nodes;
        string eventName;
        try
        {
            List<Field> fields = ParseSchema(schema, schemaSize, out eventName);
            nodes = ReadPayload(reader, fields, new Budget());
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException
                                       or InvalidOperationException or IndexOutOfRangeException)
        {
            reader.Reset();
            return false;
        }

        EventMetadata metadata = new(record->EventHeader.ProviderId, record->EventHeader.EventDescriptor.Id,
            record->EventHeader.EventDescriptor.Version, eventName,
            nodes.Select(n => n.Field.Metadata).ToArray());

        writer.WriteEventBegin(metadata, runtimeMetadata);
        WriteNodes(writer, nodes);
        writer.WriteEventEnd();
        return true;
    }

    private static List<Field> ParseSchema(byte* data, int size, out string eventName)
    {
        int position = 0;

        if (size < 3 || *(ushort*)data > size)
        {
            throw new FormatException("TraceLogging schema is truncated.");
        }

        int end = Math.Min(size, *(ushort*)data);
        position = 2;
        SkipVarint(data, ref position, end);
        eventName = ReadUtf8(data, ref position, end);

        List<Field> fields = [];
        while (position < end)
        {
            fields.Add(ParseField(data, ref position, end, 1));
        }

        return fields;
    }

    private static Field ParseField(byte* data, ref int position, int end, int depth)
    {
        if (depth > MaxSchemaDepth)
        {
            throw new FormatException("TraceLogging schema is nested too deeply.");
        }

        Field field = new() { Name = ReadUtf8(data, ref position, end) };

        if (position >= end)
        {
            throw new FormatException("TraceLogging field has no type.");
        }

        byte inType = data[position++];
        field.Type = (InType)(inType & InTypeMask);
        if (!Enum.IsDefined(field.Type))
        {
            throw new FormatException($"Unknown TraceLogging type {inType & InTypeMask}.");
        }

        if ((inType & InTypeChain) != 0)
        {
            if (position >= end)
            {
                throw new FormatException("TraceLogging field is truncated.");
            }

            byte outType = data[position++];
            field.OutType = (byte)(outType & OutTypeMask);
            if ((outType & OutTypeChain) != 0)
            {
                SkipVarint(data, ref position, end);
            }
        }

        // Bits 0x60 are a count kind: 0x20 fixed, 0x40 variable, 0x60 custom serialization.
        // Custom serialization's next ushort is a type-info size, not an array count. The value is laid out as the
        // base in-type says; the type info is kept for an optional registered decoder.
        switch ((byte)(inType & (InTypeConstantCount | InTypeVariableCount)))
        {
            case InTypeConstantCount | InTypeVariableCount:
                if (position + 2 > end)
                {
                    throw new FormatException("TraceLogging type info size is truncated.");
                }

                int typeInfoSize = *(ushort*)(data + position);
                position += 2;
                if (typeInfoSize > end - position)
                {
                    throw new FormatException("TraceLogging type info is truncated.");
                }

                field.TypeInfo = new ReadOnlySpan<byte>(data + position, typeInfoSize).ToArray();
                position += typeInfoSize;
                break;
            case InTypeConstantCount:
                if (position + 2 > end)
                {
                    throw new FormatException("TraceLogging array count is truncated.");
                }

                field.IsArray = true;
                field.ConstantCount = *(ushort*)(data + position);
                position += 2;
                break;
            case InTypeVariableCount:
                field.IsArray = true;
                field.IsVariableArray = true;
                break;
        }

        if (field.Type == InType.Struct)
        {
            // for structs the out type carries the number of member fields
            for (int i = 0; i < field.OutType; i++)
            {
                field.Children.Add(ParseField(data, ref position, end, depth + 1));
            }
        }

        field.Metadata = new PropertyMetadata(ToTdhIn(field), ToTdhOut(field), field.Name, false,
            field.Type == InType.Struct, field.Children.Count, null);
        return field;
    }

    private static void SkipVarint(byte* data, ref int position, int end)
    {
        while (true)
        {
            if (position >= end)
            {
                throw new FormatException("TraceLogging tag is truncated.");
            }

            if ((data[position++] & 0x80) == 0)
            {
                return;
            }
        }
    }

    private static string ReadUtf8(byte* data, ref int position, int end)
    {
        int start = position;
        while (position < end && data[position] != 0)
        {
            position++;
        }

        if (position >= end)
        {
            throw new FormatException("TraceLogging name is not terminated.");
        }

        string value = Encoding.UTF8.GetString(data + start, position - start);
        position++;
        return value;
    }

    private static _TDH_IN_TYPE ToTdhIn(Field field)
    {
        return field.Type switch
        {
            InType.UnicodeString => _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            InType.AnsiString => _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING,
            InType.Int8 => _TDH_IN_TYPE.TDH_INTYPE_INT8,
            InType.UInt8 => _TDH_IN_TYPE.TDH_INTYPE_UINT8,
            InType.Int16 => _TDH_IN_TYPE.TDH_INTYPE_INT16,
            InType.UInt16 => _TDH_IN_TYPE.TDH_INTYPE_UINT16,
            InType.Int32 => _TDH_IN_TYPE.TDH_INTYPE_INT32,
            InType.UInt32 => _TDH_IN_TYPE.TDH_INTYPE_UINT32,
            InType.Int64 => _TDH_IN_TYPE.TDH_INTYPE_INT64,
            InType.UInt64 => _TDH_IN_TYPE.TDH_INTYPE_UINT64,
            InType.Float => _TDH_IN_TYPE.TDH_INTYPE_FLOAT,
            InType.Double => _TDH_IN_TYPE.TDH_INTYPE_DOUBLE,
            InType.Bool32 => _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN,
            InType.Binary or InType.CountedBinary => _TDH_IN_TYPE.TDH_INTYPE_BINARY,
            InType.Guid => _TDH_IN_TYPE.TDH_INTYPE_GUID,
            InType.Pointer => _TDH_IN_TYPE.TDH_INTYPE_POINTER,
            InType.FileTime => _TDH_IN_TYPE.TDH_INTYPE_FILETIME,
            InType.SystemTime => _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME,
            InType.Sid => _TDH_IN_TYPE.TDH_INTYPE_SID,
            InType.HexInt32 => _TDH_IN_TYPE.TDH_INTYPE_HEXINT32,
            InType.HexInt64 => _TDH_IN_TYPE.TDH_INTYPE_HEXINT64,
            InType.CountedString => _TDH_IN_TYPE.TDH_INTYPE_COUNTEDSTRING,
            InType.CountedAnsiString => _TDH_IN_TYPE.TDH_INTYPE_COUNTEDANSISTRING,
            _ => _TDH_IN_TYPE.TDH_INTYPE_NULL
        };
    }

    private static _TDH_OUT_TYPE ToTdhOut(Field field)
    {
        return field.Type switch
        {
            InType.UnicodeString or InType.AnsiString or InType.CountedString or InType.CountedAnsiString =>
                _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            InType.Bool32 => _TDH_OUT_TYPE.TDH_OUTTYPE_BOOLEAN,
            InType.UInt8 when field.OutType == OutTypeBoolean => _TDH_OUT_TYPE.TDH_OUTTYPE_BOOLEAN,
            InType.Guid => _TDH_OUT_TYPE.TDH_OUTTYPE_GUID,
            InType.FileTime or InType.SystemTime => _TDH_OUT_TYPE.TDH_OUTTYPE_DATETIME,
            InType.HexInt32 => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT32,
            InType.HexInt64 or InType.Pointer => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT64,
            _ => _TDH_OUT_TYPE.TDH_OUTTYPE_NULL
        };
    }

    private sealed class Budget
    {
        public int Nodes;
    }

    private static List<Node> ReadPayload(EventRecordReader reader, List<Field> fields, Budget budget)
    {
        List<Node> nodes = new(fields.Count);
        foreach (Field field in fields)
        {
            nodes.Add(ReadNode(reader, field, budget));
        }

        return nodes;
    }

    private static Node ReadNode(EventRecordReader reader, Field field, Budget budget)
    {
        Charge(budget, 1);
        Node node = new(field);

        if (!field.IsArray)
        {
            ReadInto(reader, field, node, null, budget);
            return node;
        }

        int count = field.ConstantCount;
        if (field.IsVariableArray)
        {
            Need(reader, sizeof(ushort));
            count = reader.ReadUInt16();
        }

        Charge(budget, count);
        node.Elements = new List<object?>(count);
        for (int i = 0; i < count; i++)
        {
            Node element = new(field);
            ReadInto(reader, field, element, node.Elements, budget);
        }

        return node;
    }

    private static void Charge(Budget budget, int count)
    {
        if (count < 0 || budget.Nodes > MaxNodes - count)
        {
            throw new FormatException("TraceLogging event exceeds the node budget.");
        }

        budget.Nodes += count;
    }

    private static void ReadInto(EventRecordReader reader, Field field, Node node, List<object?>? elements,
        Budget budget)
    {
        object? value;

        if (field.Type == InType.Struct)
        {
            node.Members = ReadPayload(reader, field.Children, budget);
            value = node;
        }
        else
        {
            value = ReadValue(reader, field);
            if (field.TypeInfo is not null && TraceLoggingCustomDecoders.TryGet(field.TypeInfo, out var decoder))
            {
                value = ApplyCustomDecoder(decoder!, field.TypeInfo, value);
            }
        }

        if (elements is null)
        {
            node.Value = value;
        }
        else
        {
            elements.Add(value);
        }
    }

    private static object? ApplyCustomDecoder(TraceLoggingCustomDecoder decoder, byte[] typeInfo, object? value)
    {
        object? decoded;
        try
        {
            decoded = decoder(typeInfo, value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new FormatException("A TraceLogging custom decoder failed.", ex);
        }

        if (decoded is null or string or byte[] or bool or sbyte or byte or short or ushort or int or uint or long
            or ulong or float or double or Guid or DateTime)
        {
            return decoded;
        }

        throw new FormatException("A TraceLogging custom decoder returned an unsupported value type.");
    }
    private static object? ReadValue(EventRecordReader reader, Field field)
    {
        object? value = ReadValueUnchecked(reader, field);
        if (reader.NativeEventRecord->UserDataLength < reader.ConsumedBytes)
        {
            throw new FormatException("TraceLogging payload is shorter than its schema.");
        }

        return value;
    }

    private static void Need(EventRecordReader reader, int bytes)
    {
        if (bytes < 0 || reader.RemainingBytes < bytes)
        {
            throw new FormatException("TraceLogging payload is shorter than its schema.");
        }
    }

    private static object? ReadValueUnchecked(EventRecordReader reader, Field field)
    {
        bool utf8 = field.OutType == OutTypeUtf8;
        switch (field.Type)
        {
            case InType.Null:
                return null;
            case InType.UnicodeString:
                return reader.ReadTerminatedUnicodeString();
            case InType.AnsiString:
                return utf8 ? reader.ReadUtf8String() : reader.ReadTerminatedAnsiString();
            case InType.Int8:
                Need(reader, sizeof(sbyte));
                return reader.ReadInt8();
            case InType.UInt8:
                Need(reader, sizeof(byte));
                return reader.ReadUInt8();
            case InType.Int16:
                Need(reader, sizeof(short));
                return reader.ReadInt16();
            case InType.UInt16:
                Need(reader, sizeof(ushort));
                return reader.ReadUInt16();
            case InType.Int32:
                Need(reader, sizeof(int));
                return reader.ReadInt32();
            case InType.UInt32:
            case InType.HexInt32:
                Need(reader, sizeof(uint));
                return reader.ReadUInt32();
            case InType.Int64:
                Need(reader, sizeof(long));
                return reader.ReadInt64();
            case InType.UInt64:
            case InType.HexInt64:
                Need(reader, sizeof(ulong));
                return reader.ReadUInt64();
            case InType.Float:
                Need(reader, sizeof(float));
                float single = reader.ReadFloat();
                if (!float.IsFinite(single))
                {
                    throw new FormatException("TraceLogging float is not finite.");
                }

                return single;
            case InType.Double:
                Need(reader, sizeof(double));
                double real = reader.ReadDouble();
                if (!double.IsFinite(real))
                {
                    throw new FormatException("TraceLogging float is not finite.");
                }

                return real;
            case InType.Bool32:
                Need(reader, sizeof(int));
                return reader.ReadInt32() != 0;
            case InType.Binary:
            case InType.CountedBinary:
                Need(reader, sizeof(ushort));
                int binaryLength = reader.ReadUInt16();
                Need(reader, binaryLength);
                return reader.ReadBinary(binaryLength);
            case InType.Guid:
                Need(reader, 16);
                return reader.ReadGuid();
            case InType.Pointer:
                Need(reader, PointerBytes(reader));
                return reader.ReadPointer();
            case InType.FileTime:
                Need(reader, sizeof(long));
                return reader.ReadFileTime();
            case InType.SystemTime:
                Need(reader, 16);
                return reader.ReadSystemTime();
            case InType.Sid:
                return reader.ReadSid();
            case InType.CountedString:
                return reader.ReadCountedString();
            case InType.CountedAnsiString:
                return utf8 ? reader.ReadCountedUtf8String() : reader.ReadCountedAnsiString();
            default:
                throw new FormatException($"TraceLogging type {field.Type} is not supported.");
        }
    }

    private static unsafe int PointerBytes(EventRecordReader reader)
    {
        return (reader.NativeEventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) != 0 ? 4 : 8;
    }

    private static void WriteNodes<T>(T writer, List<Node> nodes) where T : IEtwWriter
    {
        foreach (Node node in nodes)
        {
            writer.WritePropertyBegin(node.Field.Metadata);

            if (node.Elements is null)
            {
                WriteValue(writer, node.Field, node.Value);
            }
            else
            {
                writer.WriteArrayBegin();
                foreach (object? element in node.Elements)
                {
                    WriteValue(writer, node.Field, element);
                }

                writer.WriteArrayEnd();
            }

            writer.WritePropertyEnd();
        }
    }

    private static void WriteValue<T>(T writer, Field field, object? value) where T : IEtwWriter
    {
        switch (value)
        {
            case null:
                writer.WriteNull();
                break;
            case Node structure:
                writer.WriteStructBegin();
                WriteNodes(writer, structure.Members!);
                writer.WriteStructEnd();
                break;
            case string s when field.Type is InType.AnsiString or InType.CountedAnsiString:
                writer.WriteAnsiString(s);
                break;
            case string s when field.Type == InType.Sid:
                writer.WriteSid(s);
                break;
            case string s:
                writer.WriteUnicodeString(s);
                break;
            case sbyte v:
                writer.WriteInt8(v);
                break;
            case byte v when field.OutType == OutTypeBoolean:
                writer.WriteBoolean(v != 0);
                break;
            case byte v:
                writer.WriteUInt8(v);
                break;
            case short v:
                writer.WriteInt16(v);
                break;
            case ushort v:
                writer.WriteUInt16(v);
                break;
            case int v when field.OutType == OutTypeBoolean:
                writer.WriteBoolean(v != 0);
                break;
            case int v:
                writer.WriteInt32(v);
                break;
            case uint v:
                writer.WriteUInt32(v);
                break;
            case long v:
                writer.WriteInt64(v);
                break;
            case ulong v when field.Type == InType.Pointer:
                writer.WritePointer(v);
                break;
            case ulong v:
                writer.WriteUInt64(v);
                break;
            case float v:
                writer.WriteFloat(v);
                break;
            case double v:
                writer.WriteDouble(v);
                break;
            case bool v:
                writer.WriteBoolean(v);
                break;
            case byte[] v:
                writer.WriteBinary(v);
                break;
            case Guid v:
                writer.WriteGuid(v);
                break;
            case DateTime v when field.Type == InType.SystemTime:
                writer.WriteSystemTime(v);
                break;
            case DateTime v:
                writer.WriteFileTime(v);
                break;
            default:
                throw new InvalidOperationException($"Unexpected value type {value.GetType()}.");
        }
    }
}
