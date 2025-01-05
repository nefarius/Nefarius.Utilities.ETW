using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer;

internal static unsafe class EventTraceOperandBuilder
{
    public static IEventTraceOperand Build(TRACE_EVENT_INFO* traceEventInfo, int metadataIndex)
    {
        return new EventTraceOperandBuilderFromTDH(traceEventInfo).Build(metadataIndex);
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

        public EventTraceOperandBuilderFromTDH(TRACE_EVENT_INFO* traceEventInfo)
        {
            this._traceEventInfo = traceEventInfo;
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
            int end = _traceEventInfo->DecodingSource == DECODING_SOURCE.DecodingSourceWPP
                ? 0
                : (int)_traceEventInfo->TopLevelPropertyCount;
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
            if (offset != 0)
            {
                item = new string((char*)&buffer[offset]).Trim();
            }

            return item;
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
            for (int i = start; i < end; ++i)
            {
                EVENT_PROPERTY_INFO* eventPropertyInfo = &eventPropertyInfoArr[i];
                string propertyName = new((char*)&buffer[eventPropertyInfo->NameOffset]);

                int structchildren = eventPropertyInfo->Anonymous1.structType.NumOfStructMembers;
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

                PWSTR mapName = null;
                Dictionary<uint, string> mapOfValues = null;
                if (eventPropertyInfo->Anonymous1.nonStructType.MapNameOffset != 0)
                {
                    EVENT_MAP_INFO* mapBuffer = null;
                    uint bufferSize;

                    EVENT_RECORD fakeEventRecord = new()
                    {
                        EventHeader = new EVENT_HEADER { ProviderId = _traceEventInfo->ProviderGuid }
                    };

                    mapName = new PWSTR((char*)&buffer[eventPropertyInfo->Anonymous1.nonStructType.MapNameOffset]);

                    PInvoke.TdhGetEventMapInformation(&fakeEventRecord, mapName, mapBuffer, &bufferSize);
                    mapBuffer = (EVENT_MAP_INFO*)Marshal.AllocHGlobal((int)bufferSize);
                    PInvoke.TdhGetEventMapInformation(&fakeEventRecord, mapName, mapBuffer, &bufferSize);

                    EVENT_MAP_INFO* mapInfo = mapBuffer;
                    if (mapInfo->Anonymous.MapEntryValueType == MAP_VALUETYPE.EVENTMAP_ENTRY_VALUETYPE_ULONG)
                    {
                        EVENT_MAP_ENTRY* mapEntry = (EVENT_MAP_ENTRY*)&mapInfo->MapEntryArray;
                        mapOfValues = new Dictionary<uint, string>();
                        for (int j = 0; j < mapInfo->EntryCount; ++j)
                        {
                            uint offset = mapEntry[j].OutputOffset;
                            if (offset > bufferSize)
                            {
                                // TODO: TDH has a bug (it seems) that is giving rogue values here
                                // We should log this
                            }
                            else
                            {
                                uint mapEntryValue = mapEntry[j].Anonymous.Value;
                                if (!mapOfValues.TryGetValue(mapEntryValue, out string mapEntryName))
                                {
                                    mapEntryName = new string((char*)&mapBuffer[offset]);
                                    mapOfValues.Add(mapEntryValue, mapEntryName);
                                }
                            }
                        }
                    }

                    Marshal.FreeHGlobal((IntPtr)mapBuffer);
                }

                /* save important information in an object */
                EventTracePropertyOperand operand = new(
                    new PropertyMetadata((_TDH_IN_TYPE)eventPropertyInfo->Anonymous1.nonStructType.InType,
                        (_TDH_OUT_TYPE)eventPropertyInfo->Anonymous1.nonStructType.OutType, propertyName, mapOfValues != null,
                        isStruct, isStruct ? structchildren : 0, new MapInformation(mapName.ToString(), mapOfValues)),
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
}