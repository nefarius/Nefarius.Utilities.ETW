using Windows.Win32;
using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Deserializer;

public readonly struct RuntimeEventMetadata
{
    private readonly unsafe EVENT_RECORD* _eventRecord;

    internal unsafe RuntimeEventMetadata(EVENT_RECORD* eventRecord)
    {
        _eventRecord = eventRecord;
    }

    public ushort Flags
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.Flags;
            }
        }
    }

    public uint ThreadId
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.ThreadId;
            }
        }
    }

    public uint ProcessId
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.ProcessId;
            }
        }
    }

    public long Timestamp
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.TimeStamp;
            }
        }
    }

    public Guid ProviderId
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.ProviderId;
            }
        }
    }

    public ushort EventId
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Id;
            }
        }
    }

    public Guid ActivityId
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.ActivityId;
            }
        }
    }

    public ushort UserDataLength
    {
        get
        {
            unsafe
            {
                return _eventRecord->UserDataLength;
            }
        }
    }

    public Guid RelatedActivityId
    {
        get
        {
            unsafe
            {
                EVENT_HEADER_EXTENDED_DATA_ITEM* extendedData = _eventRecord->ExtendedData;
                for (int i = 0; i < _eventRecord->ExtendedDataCount; ++i)
                {
                    if (extendedData[i].ExtType == PInvoke.EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID)
                    {
                        return *(Guid*)extendedData[i].DataPtr;
                    }
                }

                return Guid.Empty;
            }
        }
    }

    public ulong ProcessorNumber
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.Anonymous.ProcessorTime;
            }
        }
    }

    // logic from: https://msdn.microsoft.com/en-us/library/windows/desktop/dd392308(v=vs.85).aspx
    public ulong[]? GetStacks(out ulong matchId)
    {
        unsafe
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM* extendedData = _eventRecord->ExtendedData;
            for (int i = 0; i < _eventRecord->ExtendedDataCount; ++i)
            {
                switch (extendedData[i].ExtType)
                {
                    case (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE32:
                        {
                            int numberOfInstructionPointers = (extendedData[i].DataSize - sizeof(ulong)) / sizeof(uint);
                            return GetStacks32(numberOfInstructionPointers, ref extendedData[i], out matchId);
                        }

                    case (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE64:
                        {
                            int numberOfInstructionPointers =
                                (extendedData[i].DataSize - sizeof(ulong)) / sizeof(ulong);
                            return GetStacks64(numberOfInstructionPointers, ref extendedData[i], out matchId);
                        }
                }
            }

            matchId = 0;
            return null;
        }
    }

    private static unsafe ulong[] GetStacks64(int numberOfInstructionPointers,
        ref EVENT_HEADER_EXTENDED_DATA_ITEM extendedData, out ulong matchId)
    {
        ulong[] retArr = new ulong[numberOfInstructionPointers];
        matchId = *(ulong*)extendedData.DataPtr;

        extendedData.DataPtr += sizeof(ulong);

        ulong* dataPtr = (ulong*)extendedData.DataPtr;

        for (int j = 0; j < numberOfInstructionPointers; ++j)
        {
            retArr[j] = *dataPtr;
            dataPtr++;
        }

        return retArr;
    }

    private static unsafe ulong[] GetStacks32(int numberOfInstructionPointers,
        ref EVENT_HEADER_EXTENDED_DATA_ITEM extendedData, out ulong matchId)
    {
        ulong[] retArr = new ulong[numberOfInstructionPointers];
        matchId = *(ulong*)extendedData.DataPtr;

        extendedData.DataPtr += sizeof(ulong);

        int* dataPtr = (int*)extendedData.DataPtr;

        for (int j = 0; j < numberOfInstructionPointers; ++j)
        {
            retArr[j] = (ulong)*dataPtr;
            dataPtr++;
        }

        return retArr;
    }
}