using System.Runtime.CompilerServices;

namespace Nefarius.Utilities.ETW.Deserializer;

internal readonly struct RuntimeEventMetadata
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

    public byte EventVersion
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Version;
            }
        }
    }

    public byte Channel
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Channel;
            }
        }
    }

    public byte Level
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Level;
            }
        }
    }

    public byte Opcode
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Opcode;
            }
        }
    }

    public ushort Task
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Task;
            }
        }
    }

    public ulong Keyword
    {
        get
        {
            unsafe
            {
                return _eventRecord->EventHeader.EventDescriptor.Keyword;
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
                if (extendedData == null)
                {
                    return Guid.Empty;
                }

                for (int i = 0; i < _eventRecord->ExtendedDataCount; ++i)
                {
                    if (extendedData[i].ExtType == PInvoke.EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID)
                    {
                        if (extendedData[i].DataSize < 16 || extendedData[i].DataPtr == 0)
                        {
                            return Guid.Empty;
                        }

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
            if (extendedData == null)
            {
                matchId = 0;
                return null;
            }

            // The items are only read, never modified: a record may be inspected more than once, and relayed
            // records are untrusted.
            for (int i = 0; i < _eventRecord->ExtendedDataCount; ++i)
            {
                ushort size = extendedData[i].DataSize;
                byte* data = (byte*)extendedData[i].DataPtr;
                if (data == null || size < sizeof(ulong))
                {
                    continue;
                }

                switch (extendedData[i].ExtType)
                {
                    case (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE32:
                        return GetStacks32((size - sizeof(ulong)) / sizeof(uint), data, out matchId);

                    case (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE64:
                        return GetStacks64((size - sizeof(ulong)) / sizeof(ulong), data, out matchId);
                }
            }

            matchId = 0;
            return null;
        }
    }

    private static unsafe ulong[] GetStacks64(int numberOfInstructionPointers, byte* data, out ulong matchId)
    {
        ulong[] retArr = new ulong[numberOfInstructionPointers];
        matchId = Unsafe.ReadUnaligned<ulong>(data);

        byte* dataPtr = data + sizeof(ulong);
        for (int j = 0; j < numberOfInstructionPointers; ++j)
        {
            retArr[j] = Unsafe.ReadUnaligned<ulong>(dataPtr);
            dataPtr += sizeof(ulong);
        }

        return retArr;
    }

    private static unsafe ulong[] GetStacks32(int numberOfInstructionPointers, byte* data, out ulong matchId)
    {
        ulong[] retArr = new ulong[numberOfInstructionPointers];
        matchId = Unsafe.ReadUnaligned<ulong>(data);

        byte* dataPtr = data + sizeof(ulong);
        for (int j = 0; j < numberOfInstructionPointers; ++j)
        {
            // sign extension is what the previous implementation did and is kept for output compatibility
            retArr[j] = (ulong)Unsafe.ReadUnaligned<int>(dataPtr);
            dataPtr += sizeof(uint);
        }

        return retArr;
    }
}
