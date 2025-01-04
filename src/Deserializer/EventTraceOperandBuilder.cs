using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Etw;

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
        private readonly ushort eventId;
        private readonly instrumentationManifest manifest;

        public TraceEventOperandBuilderFromXml(instrumentationManifest manifest, ushort eventId)
        {
            this.manifest = manifest;
            this.eventId = eventId;
        }

        public IEventTraceOperand Build(int eventMetadataTableIndex)
        {
            foreach (object? instrumentationManifestTypeItem in manifest.Items)
            {
                InstrumentationType? instrumentationType = instrumentationManifestTypeItem as InstrumentationType;
                if (instrumentationType != null)
                {
                    foreach (object? instrumentationTypeItem in instrumentationType.Items)
                    {
                        EventsType? eventTypes = instrumentationTypeItem as EventsType;
                        if (eventTypes != null)
                        {
                            foreach (object? eventTypeItem in eventTypes.Items)
                            {
                                ProviderType? providerType = eventTypeItem as ProviderType;
                                if (providerType != null)
                                {
                                    string? providerName = providerType.name;
                                    Guid providerGuid = new(providerType.guid);

                                    foreach (object? providerTypeItem in providerType.Items)
                                    {
                                        DefinitionType? definitionType = providerTypeItem as DefinitionType;
                                        if (definitionType != null)
                                        {
                                            foreach (EventDefinitionType definitionTypeItem in definitionType.Items)
                                            {
                                                if (string.Equals(definitionTypeItem.value, eventId.ToString("D")))
                                                {
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
                                                        TemplateListType? templateList = item as TemplateListType;
                                                        if (templateList != null)
                                                        {
                                                            foreach (TemplateItemType? templateTid in templateList
                                                                         .template)
                                                            {
                                                                if (string.Equals(templateTid.tid, template))
                                                                {
                                                                    int i = 0;
                                                                    foreach (DataDefinitionType propertyItem in
                                                                             templateTid.Items)
                                                                    {
                                                                        _TDH_IN_TYPE inType =
                                                                            Extensions.ToTdhInType(propertyItem.inType
                                                                                .Name);
                                                                        _TDH_OUT_TYPE outType =
                                                                            _TDH_OUT_TYPE.TDH_OUTTYPE_NOPRINT;
                                                                        PropertyMetadata metadata =
                                                                            new(inType, outType,
                                                                                propertyItem.name, false, false, 0,
                                                                                null);
                                                                        properties.Add(
                                                                            new EventTracePropertyOperand(metadata, i++,
                                                                                false, false, false, false, false));
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }

                                                    EventTraceOperand operand = new(
                                                        new EventMetadata(
                                                            providerGuid,
                                                            eventId,
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
        private readonly List<IEventTracePropertyOperand> flatPropertyList = new();
        private readonly TRACE_EVENT_INFO* traceEventInfo;

        public EventTraceOperandBuilderFromTDH(TRACE_EVENT_INFO* traceEventInfo)
        {
            this.traceEventInfo = traceEventInfo;
        }

        public IEventTraceOperand Build(int eventMetadataTableIndex)
        {
            byte* buffer = (byte*)traceEventInfo;
            EVENT_PROPERTY_INFO* eventPropertyInfoArr = (EVENT_PROPERTY_INFO*)&traceEventInfo->EventPropertyInfoArray;

            string provider = BuildName("Provider", traceEventInfo->ProviderGuid.ToString(),
                traceEventInfo->ProviderNameOffset);
            string task = BuildName("EventID", traceEventInfo->EventDescriptor.Id.ToString(),
                traceEventInfo->TaskNameOffset);
            string opcode = BuildName("Opcode", traceEventInfo->EventDescriptor.Opcode.ToString(),
                traceEventInfo->OpcodeNameOffset);

            //WPP properties not supported
            int end = traceEventInfo->DecodingSource == DECODING_SOURCE.DecodingSourceWPP
                ? 0
                : (int)traceEventInfo->TopLevelPropertyCount;
            List<EventTracePropertyOperand> topLevelOperands = IterateProperties(buffer, 0, end, eventPropertyInfoArr);
            return new EventTraceOperand(
                new EventMetadata(traceEventInfo->ProviderGuid, traceEventInfo->EventDescriptor.Id,
                    traceEventInfo->EventDescriptor.Version,
                    provider + "/" + task + "/" + opcode, flatPropertyList.Select(t => t.Metadata).ToArray()),
                eventMetadataTableIndex, topLevelOperands);
        }

        private string BuildName(string prefix, string value, uint offset)
        {
            byte* buffer = (byte*)traceEventInfo;
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

                int structchildren = eventPropertyInfo->StructType.NumOfStructMembers;
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
                if (!isFixedArray && !isVariableArray && eventPropertyInfo->count > 1)
                {
                    isFixedArray = true;
                }

                PWSTR mapName = null;
                Dictionary<uint, string> mapOfValues = null;
                if (eventPropertyInfo->NonStructType.MapNameOffset != 0)
                {
                    EVENT_MAP_INFO* mapBuffer = null;
                    uint bufferSize;

                    EVENT_RECORD fakeEventRecord = new()
                    {
                        EventHeader = new EVENT_HEADER { ProviderId = traceEventInfo->ProviderGuid }
                    };

                    mapName = new PWSTR((char*)&buffer[eventPropertyInfo->NonStructType.MapNameOffset]);

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
                                // TDH has a bug (it seems) that is giving rogue values here
                                // We should log this
                            }
                            else
                            {
                                uint mapEntryValue = mapEntry[j].Value;
                                string mapEntryName;
                                if (!mapOfValues.TryGetValue(mapEntryValue, out mapEntryName))
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
                    new PropertyMetadata((_TDH_IN_TYPE)eventPropertyInfo->NonStructType.InType,
                        (_TDH_OUT_TYPE)eventPropertyInfo->NonStructType.OutType, propertyName, mapOfValues != null,
                        isStruct, isStruct ? structchildren : 0, new MapInformation(mapName.ToString(), mapOfValues)),
                    i,
                    isVariableArray,
                    isFixedArray,
                    isVariableLength,
                    isFixedLength,
                    isWbemXmlFragment);

                flatPropertyList.Add(operand);
                operands.Add(operand);
                returnList.Add(operand);

                /* if this references a previous field, we need to capture that as a local */
                if (isVariableArray)
                {
                    EventTracePropertyOperand reference = operands[eventPropertyInfo->countPropertyIndex];
                    reference.IsReferencedByOtherProperties = true;
                    operand.SetVariableArraySize(reference);
                }
                else if (isFixedArray)
                {
                    operand.SetFixedArraySize(eventPropertyInfo->count);
                }

                /* if this references a previous field, we need to capture that as a local */
                if (isVariableLength)
                {
                    EventTracePropertyOperand reference = operands[eventPropertyInfo->lengthPropertyIndex];
                    reference.IsReferencedByOtherProperties = true;
                    operand.SetVariableLengthSize(reference);
                }
                else if (isFixedLength)
                {
                    operand.SetFixedLengthSize(eventPropertyInfo->length);
                }

                if ((eventPropertyInfo->Flags & PROPERTY_FLAGS.PropertyStruct) == PROPERTY_FLAGS.PropertyStruct)
                {
                    List<EventTracePropertyOperand> innerProps = IterateProperties(
                        buffer,
                        operands,
                        eventPropertyInfo->StructType.StructStartIndex,
                        eventPropertyInfo->StructType.StructStartIndex +
                        eventPropertyInfo->StructType.NumOfStructMembers,
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