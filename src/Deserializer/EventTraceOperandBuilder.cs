using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer;

internal static unsafe class EventTraceOperandBuilder
{
    public static IEventTraceOperand Build(TRACE_EVENT_INFO* traceEventInfo, uint infoSize, int metadataIndex)
    {
        return new EventTraceOperandBuilderFromTDH(traceEventInfo, infoSize).Build(metadataIndex);
    }

    /// <summary>
    ///     True when the buffer holds the whole <c>TRACE_EVENT_INFO</c> and every declared property record, every struct
    ///     member range lies inside that array, every count or length index points at an operand already visited, and no
    ///     struct range re-enters a struct still being expanded. A schema that fails this is not decoded: the caller caches
    ///     whatever operand it builds, so a clamped range would become a permanent parser.
    /// </summary>
    internal static bool IsDecodableSchema(TRACE_EVENT_INFO* info, uint length)
    {
        if (info == null || length < (uint)sizeof(TRACE_EVENT_INFO) || info->PropertyCount > int.MaxValue
            || info->TopLevelPropertyCount > info->PropertyCount)
        {
            return false;
        }

        ulong arrayOffset = (ulong)((byte*)&info->EventPropertyInfoArray - (byte*)info);
        ulong required = arrayOffset + (ulong)info->PropertyCount * (ulong)sizeof(EVENT_PROPERTY_INFO);
        if (required > length)
        {
            return false;
        }

        int count = (int)info->PropertyCount;
        EVENT_PROPERTY_INFO* properties = (EVENT_PROPERTY_INFO*)&info->EventPropertyInfoArray;
        HashSet<int> expanding = new();
        int visited = 0;
        return RangesFit(0, (int)info->TopLevelPropertyCount, 0);

        // Struct nesting deeper than this is rejected. The operand walk is recursive, and a chain this deep is not a
        // schema this decoder can traverse safely. Real manifest and TDH structs nest far less than this.
        bool RangesFit(int start, int end, int depth)
        {
            if ((uint)start > (uint)end || end > count || depth > 64)
            {
                return false;
            }

            for (int i = start; i < end; i++)
            {
                if (!expanding.Add(i))
                {
                    return false;
                }

                visited++;
                EVENT_PROPERTY_INFO* property = &properties[i];
                if ((property->Flags & PROPERTY_FLAGS.PropertyParamCount) == PROPERTY_FLAGS.PropertyParamCount
                    && property->Anonymous2.countPropertyIndex >= visited)
                {
                    return false;
                }

                if ((property->Flags & PROPERTY_FLAGS.PropertyParamLength) == PROPERTY_FLAGS.PropertyParamLength
                    && property->Anonymous3.lengthPropertyIndex >= visited)
                {
                    return false;
                }

                if ((property->Flags & PROPERTY_FLAGS.PropertyStruct) == PROPERTY_FLAGS.PropertyStruct
                    && property->Anonymous1.structType.NumOfStructMembers > 0)
                {
                    int memberStart = property->Anonymous1.structType.StructStartIndex;
                    int memberEnd = memberStart + property->Anonymous1.structType.NumOfStructMembers;
                    if (!RangesFit(memberStart, memberEnd, depth + 1))
                    {
                        return false;
                    }
                }

                expanding.Remove(i);
            }

            return true;
        }
    }

    /// <summary>
    ///     Reads a UTF-16 string that TDH places at a byte <paramref name="offset" /> inside a buffer of
    ///     <paramref name="size" /> bytes. The offset and the terminator must both lie inside the buffer.
    ///     Metadata that violates this yields <see langword="null" /> instead of reading past the allocation.
    /// </summary>
    internal static string? ReadBoundedString(byte* buffer, uint size, uint offset)
    {
        if (offset == 0 || offset >= size || (offset & 1) != 0)
        {
            return null;
        }

        int maxChars = (int)((size - offset) / sizeof(char));
        char* start = (char*)(buffer + offset);
        for (int length = 0; length < maxChars; length++)
        {
            if (start[length] == '\0')
            {
                return new string(start, 0, length);
            }
        }

        return null;
    }

    public static IEventTraceOperand Build(instrumentationManifest manifest, ushort eventId, int metadataIndex)
    {
        return new TraceEventOperandBuilderFromXml(manifest, eventId).Build(metadataIndex);
    }

    private sealed class TraceEventOperandBuilderFromXml
    {
        private readonly ushort _eventId;
        private readonly instrumentationManifest _manifest;

        public TraceEventOperandBuilderFromXml(instrumentationManifest manifest, ushort eventId)
        {
            _manifest = manifest;
            _eventId = eventId;
        }

        public IEventTraceOperand Build(int eventMetadataTableIndex)
        {
            foreach (object? instrumentationManifestTypeItem in _manifest.Items)
            {
                if (instrumentationManifestTypeItem is not InstrumentationType instrumentationType)
                {
                    continue;
                }

                foreach (object? instrumentationTypeItem in instrumentationType.Items)
                {
                    if (instrumentationTypeItem is not EventsType eventTypes)
                    {
                        continue;
                    }

                    foreach (object? eventTypeItem in eventTypes.Items)
                    {
                        if (eventTypeItem is not ProviderType providerType)
                        {
                            continue;
                        }

                        string? providerName = providerType.name;
                        Guid providerGuid = new(providerType.guid);

                        foreach (object? providerTypeItem in providerType.Items)
                        {
                            if (providerTypeItem is not DefinitionType definitionType)
                            {
                                continue;
                            }

                            foreach (EventDefinitionType definitionTypeItem in definitionType.Items)
                            {
                                if (!string.Equals(definitionTypeItem.value, _eventId.ToString("D")))
                                {
                                    continue;
                                }

                                string task = definitionTypeItem.task == null
                                    ? string.IsNullOrEmpty(definitionTypeItem.symbol)
                                        ? "Task"
                                        : definitionTypeItem.symbol
                                    : definitionTypeItem.task.Name;
                                string opcode = definitionTypeItem.opcode == null
                                    ? string.Empty
                                    : definitionTypeItem.opcode.Name;
                                string version = definitionTypeItem.version ?? "0";
                                string template = definitionTypeItem.template;

                                string name = providerName + "/" + task +
                                              (opcode == string.Empty
                                                  ? string.Empty
                                                  : "/" + opcode);
                                List<IEventTracePropertyOperand> properties = new();

                                foreach (object? item in providerType.Items)
                                {
                                    if (item is not TemplateListType templateList)
                                    {
                                        continue;
                                    }

                                    foreach (TemplateItemType? templateTid in templateList
                                                 .template)
                                    {
                                        if (!string.Equals(templateTid.tid, template))
                                        {
                                            continue;
                                        }

                                        int i = 0;
                                        properties.AddRange((from DataDefinitionType propertyItem in templateTid.Items
                                            let inType = Extensions.ToTdhInType(propertyItem.inType.Name)
                                            let outType = _TDH_OUT_TYPE.TDH_OUTTYPE_NOPRINT
                                            select new PropertyMetadata(inType, outType, propertyItem.name, false,
                                                false, 0, null)
                                            into metadata
                                            select new EventTracePropertyOperand(metadata, i++, false, false, false,
                                                false, false)));
                                    }
                                }

                                EventTraceOperand operand = new(
                                    new EventMetadata(
                                        providerGuid,
                                        _eventId,
                                        byte.Parse(version),
                                        name,
                                        properties.Select(t => t.Metadata).ToArray()),
                                    eventMetadataTableIndex,
                                    properties);
                                return operand;
                            }
                        }
                    }
                }
            }

            return null;
        }
    }

    private sealed class EventTraceOperandBuilderFromTDH
    {
        private readonly List<IEventTracePropertyOperand> _flatPropertyList = new();
        private readonly TRACE_EVENT_INFO* _traceEventInfo;
        private readonly uint _infoSize;
        private int _propertyLimit;

        public EventTraceOperandBuilderFromTDH(TRACE_EVENT_INFO* traceEventInfo, uint infoSize)
        {
            this._traceEventInfo = traceEventInfo;
            this._infoSize = infoSize;
        }

        public IEventTraceOperand Build(int eventMetadataTableIndex)
        {
            byte* buffer = (byte*)_traceEventInfo;
            EVENT_PROPERTY_INFO* eventPropertyInfoArr = (EVENT_PROPERTY_INFO*)&_traceEventInfo->EventPropertyInfoArray;

            string provider = BuildName("Provider", _traceEventInfo->ProviderGuid.ToString(),
                _traceEventInfo->ProviderNameOffset);
            string task = BuildName("EventID", _traceEventInfo->EventDescriptor.Id.ToString(),
                _traceEventInfo->TaskNameOffset);
            string opcode = BuildName("Opcode", _traceEventInfo->EventDescriptor.Opcode.ToString(),
                _traceEventInfo->OpcodeNameOffset);

            // TODO: WPP properties not supported
            // The property array must lie inside the buffer TDH filled in.
            long arrayOffset = (byte*)eventPropertyInfoArr - buffer;
            long capacity = (_infoSize - arrayOffset) / sizeof(EVENT_PROPERTY_INFO);
            // The record count and the top-level count must both be honoured, or trailing name strings would be
            // parsed as property records. Inconsistent counts use the smallest value.
            _propertyLimit = arrayOffset > _infoSize
                ? 0
                : (int)Math.Min(Math.Min(_traceEventInfo->PropertyCount, capacity), int.MaxValue);
            int end = _traceEventInfo->DecodingSource == DECODING_SOURCE.DecodingSourceWPP
                ? 0
                : (int)Math.Min(_traceEventInfo->TopLevelPropertyCount, _propertyLimit);
            List<EventTracePropertyOperand> topLevelOperands = IterateProperties(buffer, 0, end, eventPropertyInfoArr);
            return new EventTraceOperand(
                new EventMetadata(_traceEventInfo->ProviderGuid, _traceEventInfo->EventDescriptor.Id,
                    _traceEventInfo->EventDescriptor.Version,
                    provider + "/" + task + "/" + opcode, _flatPropertyList.Select(t => t.Metadata).ToArray()),
                eventMetadataTableIndex, topLevelOperands);
        }

        private string BuildName(string prefix, string value, uint offset)
        {
            byte* buffer = (byte*)_traceEventInfo;
            string item = prefix + "(" + value + ")";
            if (ReadBoundedString(buffer, _infoSize, offset) is { } name)
            {
                item = name.Trim();
            }

            return item;
        }

        /// <summary>
        ///     Resolves a value map. TDH fills <c>EVENT_MAP_INFO</c> and its strings in one buffer; every offset it
        ///     returns is a byte offset from the start of that buffer and is checked against its size.
        /// </summary>
        private Dictionary<uint, string>? ReadMap(string mapName)
        {
            EVENT_RECORD fakeEventRecord = new()
            {
                EventHeader = new EVENT_HEADER { ProviderId = _traceEventInfo->ProviderGuid }
            };

            uint bufferSize = 0;
            fixed (char* mapNamePointer = mapName)
            {
                PWSTR name = new(mapNamePointer);
                if (PInvoke.TdhGetEventMapInformation(&fakeEventRecord, name, null, &bufferSize)
                    != (uint)WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER
                    || bufferSize < (uint)sizeof(EVENT_MAP_INFO)
                    || bufferSize > int.MaxValue)
                {
                    return null;
                }

                byte* raw = (byte*)Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    EVENT_MAP_INFO* mapInfo = (EVENT_MAP_INFO*)raw;
                    if (PInvoke.TdhGetEventMapInformation(&fakeEventRecord, name, mapInfo, &bufferSize)
                        != (uint)WIN32_ERROR.ERROR_SUCCESS
                        || mapInfo->Anonymous.MapEntryValueType != MAP_VALUETYPE.EVENTMAP_ENTRY_VALUETYPE_ULONG)
                    {
                        return null;
                    }

                    byte* entries = (byte*)&mapInfo->MapEntryArray;
                    long entriesOffset = entries - raw;
                    long capacity = (bufferSize - entriesOffset) / sizeof(EVENT_MAP_ENTRY);
                    long count = Math.Min(mapInfo->EntryCount, capacity);
                    Dictionary<uint, string> values = new();
                    EVENT_MAP_ENTRY* mapEntry = (EVENT_MAP_ENTRY*)entries;
                    for (long j = 0; j < count; ++j)
                    {
                        if (ReadBoundedString(raw, bufferSize, mapEntry[j].OutputOffset) is { } entryName)
                        {
                            values.TryAdd(mapEntry[j].Anonymous.Value, entryName);
                        }
                    }

                    return values;
                }
                finally
                {
                    Marshal.FreeHGlobal((IntPtr)raw);
                }
            }
        }

        private List<EventTracePropertyOperand> IterateProperties(byte* buffer, int start, int end,
            EVENT_PROPERTY_INFO* eventPropertyInfoArr)
        {
            List<EventTracePropertyOperand> operands = new();
            return IterateProperties(buffer, operands, start, end, eventPropertyInfoArr);
        }

        private List<EventTracePropertyOperand> IterateProperties(byte* buffer,
            List<EventTracePropertyOperand> operands, int start, int end, EVENT_PROPERTY_INFO* eventPropertyInfoArr)
        {
            List<EventTracePropertyOperand> returnList = new();
            end = Math.Min(end, _propertyLimit);
            for (int i = start; i < end; ++i)
            {
                EVENT_PROPERTY_INFO* eventPropertyInfo = &eventPropertyInfoArr[i];
                if (i < 0 || (byte*)(eventPropertyInfo + 1) > buffer + _infoSize)
                {
                    break;
                }

                string propertyName = ReadBoundedString(buffer, _infoSize, eventPropertyInfo->NameOffset)
                                      ?? "Property" + i;

                int structChildren = eventPropertyInfo->Anonymous1.structType.NumOfStructMembers;
                bool isStruct = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyStruct) ==
                                PROPERTY_FLAGS.PropertyStruct;
                bool isVariableArray = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyParamCount) ==
                                       PROPERTY_FLAGS.PropertyParamCount;
                bool isFixedArray = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyParamFixedCount) ==
                                    PROPERTY_FLAGS.PropertyParamFixedCount;
                bool isVariableLength = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyParamLength) ==
                                        PROPERTY_FLAGS.PropertyParamLength;
                bool isFixedLength = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyParamFixedLength) ==
                                     PROPERTY_FLAGS.PropertyParamFixedLength;
                bool isWbemXmlFragment = (eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyWBEMXmlFragment) ==
                                         PROPERTY_FLAGS.PropertyWBEMXmlFragment;

                // NOTE: Do not remove this special case, there are cases like this, we just assume it's a fixed array
                if (!isFixedArray && !isVariableArray && eventPropertyInfo->Anonymous2.count > 1)
                {
                    isFixedArray = true;
                }

                string? mapName = null;
                Dictionary<uint, string>? mapOfValues = null;
                if (eventPropertyInfo->Anonymous1.nonStructType.MapNameOffset != 0
                    && !isStruct
                    && ReadBoundedString(buffer, _infoSize, eventPropertyInfo->Anonymous1.nonStructType.MapNameOffset) is { } mapNameText)
                {
                    mapName = mapNameText;
                    mapOfValues = ReadMap(mapNameText);
                }

                /* save important information in an object */
                EventTracePropertyOperand operand = new(
                    new PropertyMetadata((_TDH_IN_TYPE)eventPropertyInfo->Anonymous1.nonStructType.InType,
                        (_TDH_OUT_TYPE)eventPropertyInfo->Anonymous1.nonStructType.OutType, propertyName, mapOfValues != null,
                        isStruct, isStruct ? structChildren : 0, new MapInformation(mapName ?? string.Empty, mapOfValues)),
                    i,
                    isVariableArray,
                    isFixedArray,
                    isVariableLength,
                    isFixedLength,
                    isWbemXmlFragment);

                _flatPropertyList.Add(operand);
                operands.Add(operand);
                returnList.Add(operand);

                /* if this references a previous field, we need to capture that as a local */
                if (isVariableArray)
                {
                    EventTracePropertyOperand reference = operands[eventPropertyInfo->Anonymous2.countPropertyIndex];
                    reference.IsReferencedByOtherProperties = true;
                    operand.SetVariableArraySize(reference);
                }
                else if (isFixedArray)
                {
                    operand.SetFixedArraySize(eventPropertyInfo->Anonymous2.count);
                }

                /* if this references a previous field, we need to capture that as a local */
                if (isVariableLength)
                {
                    EventTracePropertyOperand reference = operands[eventPropertyInfo->Anonymous3.lengthPropertyIndex];
                    reference.IsReferencedByOtherProperties = true;
                    operand.SetVariableLengthSize(reference);
                }
                else if (isFixedLength)
                {
                    operand.SetFixedLengthSize(eventPropertyInfo->Anonymous3.length);
                }

                if ((eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyStruct) == PROPERTY_FLAGS.PropertyStruct)
                {
                    List<EventTracePropertyOperand> innerProps = IterateProperties(
                        buffer,
                        operands,
                        eventPropertyInfo->Anonymous1.structType.StructStartIndex,
                        eventPropertyInfo->Anonymous1.structType.StructStartIndex +
                        eventPropertyInfo->Anonymous1.structType.NumOfStructMembers,
                        eventPropertyInfoArr);

                    foreach (EventTracePropertyOperand innerProp in innerProps)
                    {
                        operand.Children.Add(innerProp);
                    }
                }
            }

            return returnList;
        }
    }
}

internal sealed class UnknownOperandBuilder : IEventTraceOperand
{
    public UnknownOperandBuilder(Guid providerGuid, int metadataTableIndex)
    {
        EventMetadataTableIndex = metadataTableIndex;
        Metadata = new EventMetadata(providerGuid, 0, 0, "UnknownProvider(" + providerGuid + ")",
            new PropertyMetadata[0]);
    }

    public int EventMetadataTableIndex { get; set; }

    public EventMetadata Metadata { get; set; }

    public IEnumerable<IEventTracePropertyOperand> EventPropertyOperands =>
        Enumerable.Empty<IEventTracePropertyOperand>();

    public override string ToString()
    {
        return Metadata.ToString();
    }
}