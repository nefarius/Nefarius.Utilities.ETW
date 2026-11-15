using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer;

internal static unsafe class EventTraceOperandBuilder
{
    public static IEventTraceOperand Build(TRACE_EVENT_INFO* traceEventInfo, uint infoSize, int metadataIndex,
        bool resolveMaps = true)
    {
        return new EventTraceOperandBuilderFromTDH(traceEventInfo, infoSize, resolveMaps).Build(metadataIndex);
    }

    /// <summary>
    ///     True when the buffer holds the whole <c>TRACE_EVENT_INFO</c> and every declared property record, every struct
    ///     member range lies inside that array, and the walk reaches each property-array index at most once.
    ///     A count or length index must name a property whose own references were already checked; the current property is
    ///     recorded only after its references pass. A schema that fails this is not decoded: the caller caches whatever
    ///     operand it builds, so a clamped or repeated range would become a permanent parser.
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
        HashSet<int> reached = new();
        HashSet<int> validated = new();
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
                if (!reached.Add(i))
                {
                    return false;
                }

                EVENT_PROPERTY_INFO* property = &properties[i];
                if ((property->Flags & PROPERTY_FLAGS.PropertyParamCount) == PROPERTY_FLAGS.PropertyParamCount
                    && !validated.Contains(property->Anonymous2.countPropertyIndex))
                {
                    return false;
                }

                if ((property->Flags & PROPERTY_FLAGS.PropertyParamLength) == PROPERTY_FLAGS.PropertyParamLength
                    && !validated.Contains(property->Anonymous3.lengthPropertyIndex))
                {
                    return false;
                }

                validated.Add(i);
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

    /// <summary>
    ///     Builds the operand for an event from an instrumentation manifest. Only the provider with the given GUID is
    ///     considered, and the event must match both id and version (an omitted manifest version means 0).
    /// </summary>
    public static IEventTraceOperand? Build(instrumentationManifest manifest, Guid providerGuid, ushort eventId,
        byte eventVersion, int metadataIndex)
    {
        return new TraceEventOperandBuilderFromXml(manifest, providerGuid, eventId, eventVersion).Build(metadataIndex);
    }

    private sealed class TraceEventOperandBuilderFromXml
    {
        private readonly ushort _eventId;
        private readonly instrumentationManifest _manifest;
        private readonly Guid _providerGuid;
        private readonly byte _eventVersion;

        public TraceEventOperandBuilderFromXml(instrumentationManifest manifest, Guid providerGuid, ushort eventId,
            byte eventVersion)
        {
            _manifest = manifest;
            _providerGuid = providerGuid;
            _eventId = eventId;
            _eventVersion = eventVersion;
        }

        private Dictionary<string, string>? _strings;

        /// <summary>
        ///     Flattens the template's members (pre-order, as TDH does) into operands. <c>count</c> and <c>length</c>
        ///     attributes become fixed sizes when numeric and references to an earlier member when they name one.
        ///     A reference that names no earlier member, or names a struct, makes the schema undecodable (false).
        ///     A struct's <c>count</c> is its member count, not an array size.
        /// </summary>
        private bool TryBuildProperties(ProviderType provider, string? template,
            out List<IEventTracePropertyOperand> topLevel, out PropertyMetadata[] flat)
        {
            topLevel = new List<IEventTracePropertyOperand>();
            flat = Array.Empty<PropertyMetadata>();
            if (string.IsNullOrEmpty(template))
            {
                return true;
            }

            Dictionary<string, MapInformation> maps = BuildMaps(provider);
            List<PropertyMetadata> flatList = new();
            List<Dictionary<string, EventTracePropertyOperand>> scopes = new();

            foreach (object? item in provider.Items)
            {
                if (item is not TemplateListType templateList)
                {
                    continue;
                }

                foreach (TemplateItemType templateItem in templateList.template)
                {
                    if (!string.Equals(templateItem.tid, template))
                    {
                        continue;
                    }

                    scopes.Clear();
                    if (!Walk(templateItem.Items ?? Array.Empty<object>(), topLevel))
                    {
                        return false;
                    }
                }
            }

            flat = flatList.ToArray();
            return true;

            bool Walk(IEnumerable<object> items, List<IEventTracePropertyOperand> target)
            {
                Dictionary<string, EventTracePropertyOperand> scope = new(StringComparer.Ordinal);
                scopes.Add(scope);
                try
                {
                    foreach (object member in items)
                    {
                        EventTracePropertyOperand operand;
                        if (member is DataDefinitionType data)
                        {
                            if (data.inType is null)
                            {
                                return false;
                            }

                            maps.TryGetValue(data.map ?? string.Empty, out MapInformation? map);
                            PropertyMetadata metadata = new(Extensions.ToTdhInType(data.inType.Name),
                                Extensions.ToTdhOutType(data.outType?.Name), data.name, map is not null, false, 0,
                                map);
                            if (!TryCreate(metadata, data.count, data.length, allowArray: true, out operand))
                            {
                                return false;
                            }

                            scope[data.name ?? string.Empty] = operand;
                            target.Add(operand);
                        }
                        else if (member is StructDefinitionType structure)
                        {
                            PropertyMetadata metadata = new(_TDH_IN_TYPE.TDH_INTYPE_NULL,
                                _TDH_OUT_TYPE.TDH_OUTTYPE_NULL, structure.name, false, true,
                                structure.data?.Length ?? 0, null);
                            if (!TryCreate(metadata, structure.count, null, allowArray: true, out operand))
                            {
                                return false;
                            }

                            scope[structure.name ?? string.Empty] = operand;
                            target.Add(operand);
                            if (!Walk(structure.data ?? Array.Empty<DataDefinitionType>(), operand.Children))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }
                finally
                {
                    scopes.RemoveAt(scopes.Count - 1);
                }
            }

            bool TryCreate(PropertyMetadata metadata, string? count, string? length, bool allowArray,
                out EventTracePropertyOperand operand)
            {
                operand = null!;
                if (!TryParseSize(count, forLength: false, out int fixedCount, out EventTracePropertyOperand? countRef) ||
                    !TryParseSize(length, forLength: true, out int fixedLength, out EventTracePropertyOperand? lengthRef))
                {
                    return false;
                }

                bool hasCount = allowArray && !string.IsNullOrWhiteSpace(count);
                bool hasLength = !string.IsNullOrWhiteSpace(length);
                operand = new EventTracePropertyOperand(metadata, flatList.Count,
                    hasCount && countRef is not null, hasCount && countRef is null,
                    hasLength && lengthRef is not null, hasLength && lengthRef is null, false);
                flatList.Add(metadata);
                if (hasCount)
                {
                    if (countRef is not null)
                    {
                        operand.SetVariableArraySize(countRef);
                    }
                    else
                    {
                        operand.SetFixedArraySize(fixedCount);
                    }
                }

                bool characters = metadata.InType == _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING;
                operand.LengthIsInCharacters = characters;
                if (hasLength)
                {
                    if (lengthRef is not null)
                    {
                        operand.SetVariableLengthSize(lengthRef);
                    }
                    else
                    {
                        operand.SetFixedLengthSize(characters ? fixedLength * 2 : fixedLength);
                    }
                }

                return true;
            }

            bool TryParseSize(string? text, bool forLength, out int value, out EventTracePropertyOperand? reference)
            {
                value = 0;
                reference = null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return true;
                }

                text = text!.Trim();
                if (int.TryParse(text, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }

                for (int i = scopes.Count - 1; i >= 0; i--)
                {
                    if (scopes[i].TryGetValue(text, out EventTracePropertyOperand? found))
                    {
                        if (found.Metadata.IsStruct || found.IsVariableArray || found.IsFixedArray ||
                            !IsSizeType(found.Metadata.InType, forLength))
                        {
                            return false;
                        }

                        found.IsReferencedByOtherProperties = true;
                        reference = found;
                        return true;
                    }
                }

                return false;
            }

            static bool IsSizeType(_TDH_IN_TYPE inType, bool forLength)
            {
                return inType switch
                {
                    _TDH_IN_TYPE.TDH_INTYPE_INT16 or _TDH_IN_TYPE.TDH_INTYPE_UINT16 or
                        _TDH_IN_TYPE.TDH_INTYPE_INT32 or _TDH_IN_TYPE.TDH_INTYPE_UINT32 => true,
                    _TDH_IN_TYPE.TDH_INTYPE_INT8 or _TDH_IN_TYPE.TDH_INTYPE_UINT8 => !forLength,
                    _ => false
                };
            }
        }

        private Dictionary<string, MapInformation> BuildMaps(ProviderType provider)
        {
            Dictionary<string, MapInformation> maps = new(StringComparer.Ordinal);
            foreach (object? item in provider.Items)
            {
                if (item is not MapType mapType)
                {
                    continue;
                }

                foreach (object? map in mapType.Items ?? Array.Empty<object>())
                {
                    string? name;
                    List<(string? Value, string? Message, string? Symbol)> entries = new();
                    switch (map)
                    {
                        case ValueMapType valueMap:
                            name = valueMap.name;
                            entries.AddRange((valueMap.map ?? Array.Empty<ValueMapValueType>()).Select(e =>
                                ((string?)e.value, (string?)e.message, (string?)e.symbol)));
                            break;
                        case BitMapType bitMap:
                            name = bitMap.name;
                            entries.AddRange((bitMap.map ?? Array.Empty<BitMapValueType>()).Select(e =>
                                ((string?)e.value, (string?)e.message, (string?)e.symbol)));
                            break;
                        default:
                            continue;
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    Dictionary<uint, string> values = new();
                    foreach ((string? value, string? message, string? symbol) in entries)
                    {
                        if (value is null || !TryParseMapValue(value, out uint key))
                        {
                            continue;
                        }

                        string text = ResolveString(message) ?? symbol ?? value;
                        values.TryAdd(key, text);
                    }

                    maps.TryAdd(name!, new MapInformation(name!, values));
                }
            }

            return maps;
        }

        private static bool TryParseMapValue(string value, out uint key)
        {
            value = value.Trim();
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.TryParse(value.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out key)
                : uint.TryParse(value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out key);
        }

        /// <summary>Resolves <c>$(string.id)</c> against the manifest string tables; other text is returned as is.</summary>
        private string? ResolveString(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }

            const string prefix = "$(string.";
            if (!message!.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith(")", StringComparison.Ordinal))
            {
                return message;
            }

            if (_strings is null)
            {
                _strings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object? top in _manifest.Items)
                {
                    if (top is not LocalizationType localization)
                    {
                        continue;
                    }

                    foreach (object? resource in localization.Items ?? Array.Empty<object>())
                    {
                        if (resource is not LocalizationTypeResources resources)
                        {
                            continue;
                        }

                        foreach (StringTableType table in resources.Items ?? Array.Empty<StringTableType>())
                        {
                            foreach (StringTableTypeString entry in table.Items ?? Array.Empty<StringTableTypeString>())
                            {
                                if (entry.id is not null && entry.value is not null)
                                {
                                    _strings.TryAdd(entry.id, entry.value);
                                }
                            }
                        }
                    }
                }
            }

            string id = message.Substring(prefix.Length, message.Length - prefix.Length - 1);
            return _strings.TryGetValue(id, out string? resolved) ? resolved : null;
        }

        public IEventTraceOperand? Build(int eventMetadataTableIndex)
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
                        if (!Guid.TryParse(providerType.guid, out Guid providerGuid) ||
                            providerGuid != _providerGuid)
                        {
                            continue;
                        }

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

                                if (!byte.TryParse(definitionTypeItem.version ?? "0", out byte definedVersion) ||
                                    definedVersion != _eventVersion)
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
                                if (!TryBuildProperties(providerType, template, out List<IEventTracePropertyOperand> properties,
                                        out PropertyMetadata[] flat))
                                {
                                    return null;
                                }
                                EventTraceOperand operand = new(
                                    new EventMetadata(
                                        providerGuid,
                                        _eventId,
                                        byte.Parse(version),
                                        name,
                                        flat),
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
        private readonly bool _resolveMaps;
        private int _propertyLimit;

        public EventTraceOperandBuilderFromTDH(TRACE_EVENT_INFO* traceEventInfo, uint infoSize, bool resolveMaps)
        {
            this._traceEventInfo = traceEventInfo;
            this._infoSize = infoSize;
            _resolveMaps = resolveMaps;
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
            // Managed decoding must not consult TDH, including on Windows. A null map leaves the property as a raw value.
            if (!_resolveMaps || !OperatingSystem.IsWindows())
            {
                return null;
            }

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

        private static bool TryOperand(EventTracePropertyOperand?[] byIndex, int index, out EventTracePropertyOperand operand)
        {
            if ((uint)index < (uint)byIndex.Length && byIndex[index] is { } found)
            {
                operand = found;
                return true;
            }

            operand = null!;
            return false;
        }

        private List<EventTracePropertyOperand> IterateProperties(byte* buffer, int start, int end,
            EVENT_PROPERTY_INFO* eventPropertyInfoArr)
        {
            EventTracePropertyOperand?[] byIndex = _propertyLimit > 0
                ? new EventTracePropertyOperand[_propertyLimit]
                : [];
            return IterateProperties(buffer, byIndex, start, end, eventPropertyInfoArr);
        }

        private List<EventTracePropertyOperand> IterateProperties(byte* buffer,
            EventTracePropertyOperand?[] byIndex, int start, int end, EVENT_PROPERTY_INFO* eventPropertyInfoArr)
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
                MapInformation? map = mapOfValues is null
                    ? null
                    : new MapInformation(mapName ?? string.Empty, mapOfValues);
                EventTracePropertyOperand operand = new(
                    new PropertyMetadata((_TDH_IN_TYPE)eventPropertyInfo->Anonymous1.nonStructType.InType,
                        (_TDH_OUT_TYPE)eventPropertyInfo->Anonymous1.nonStructType.OutType, propertyName, map is not null,
                        isStruct, isStruct ? structChildren : 0, map),
                    i,
                    isVariableArray,
                    isFixedArray,
                    isVariableLength,
                    isFixedLength,
                    isWbemXmlFragment);

                _flatPropertyList.Add(operand);
                returnList.Add(operand);

                /* Count and length indexes are property-array indexes, not positions in the walk. */
                if (isVariableArray)
                {
                    if (TryOperand(byIndex, eventPropertyInfo->Anonymous2.countPropertyIndex, out EventTracePropertyOperand countReference))
                    {
                        countReference.IsReferencedByOtherProperties = true;
                        operand.SetVariableArraySize(countReference);
                    }
                }
                else if (isFixedArray)
                {
                    operand.SetFixedArraySize(eventPropertyInfo->Anonymous2.count);
                }

                if (isVariableLength)
                {
                    if (TryOperand(byIndex, eventPropertyInfo->Anonymous3.lengthPropertyIndex, out EventTracePropertyOperand lengthReference))
                    {
                        lengthReference.IsReferencedByOtherProperties = true;
                        operand.SetVariableLengthSize(lengthReference);
                    }
                }
                else if (isFixedLength)
                {
                    operand.SetFixedLengthSize(eventPropertyInfo->Anonymous3.length);
                }

                if ((uint)i < (uint)byIndex.Length)
                {
                    byIndex[i] = operand;
                }

                if ((eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyStruct) == PROPERTY_FLAGS.PropertyStruct)
                {
                    List<EventTracePropertyOperand> innerProps = IterateProperties(
                        buffer,
                        byIndex,
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