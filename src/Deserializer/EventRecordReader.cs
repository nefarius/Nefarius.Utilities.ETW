using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Nefarius.Utilities.ETW.Deserializer;

internal readonly struct EventRecordReader
{
    private readonly unsafe EVENT_RECORD* _eventRecord;

    internal unsafe EventRecordReader(EVENT_RECORD* eventRecord)
    {
        _eventRecord = eventRecord;
    }

    private unsafe void IncrementBy(ref void* ptr, int value)
    {
        ptr = (byte*)ptr + value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadUnicodeString()
    {
        int length = 0;
        byte* ptr = (byte*)_eventRecord->UserData;
        long maxLength = _eventRecord->UserDataLength -
                         ((byte*)_eventRecord->UserData - (byte*)_eventRecord->UserContext);

        while (!(ptr[length] == 0 && ptr[length + 1] == 0) && length < maxLength)
        {
            ++length;
        }

        string value = new((char*)_eventRecord->UserData, 0, (length + 1) / 2);
        IncrementBy(ref _eventRecord->UserData, (value.Length + 1) * 2); // +2 (via the multiplication)
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUnicodeString(short length)
    {
        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUnicodeString(ushort length)
    {
        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUnicodeString(int length)
    {
        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUnicodeString(uint length)
    {
        return ReadUnicodeStringHelper((int)length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadAnsiString()
    {
        int length = 0;
        byte* ptr = (byte*)_eventRecord->UserData;
        long maxLength = _eventRecord->UserDataLength -
                         ((byte*)_eventRecord->UserData - (byte*)_eventRecord->UserContext);
        while (ptr[length] != 0 && length < maxLength)
        {
            ++length;
        }

        char[] arr = new char[length];
        for (int i = 0; i < length; ++i)
        {
            arr[i] = (char)*ptr++;
        }

        IncrementBy(ref _eventRecord->UserData, length + 1); // +1 for null terminator
        return new string(arr);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadAnsiString(short length)
    {
        return ReadAnsiStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadAnsiString(ushort length)
    {
        return ReadAnsiStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadAnsiString(int length)
    {
        return ReadAnsiStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadAnsiString(uint length)
    {
        return ReadAnsiStringHelper((int)length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT8
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe sbyte ReadInt8()
    {
        sbyte value = *(sbyte*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(sbyte));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT8
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte ReadUInt8()
    {
        byte value = *(byte*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(byte));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT16
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe short ReadInt16()
    {
        short value = *(short*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(short));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT16
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ushort ReadUInt16()
    {
        ushort value = *(ushort*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(ushort));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT32
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int ReadInt32()
    {
        int value = *(int*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(int));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT32
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe uint ReadUInt32()
    {
        uint value = *(uint*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(uint));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT64
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long ReadInt64()
    {
        long value = *(long*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(long));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT64
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong ReadUInt64()
    {
        ulong value = *(ulong*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(ulong));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_FLOAT
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float ReadFloat()
    {
        float value = *(float*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(float));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_DOUBLE
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe double ReadDouble()
    {
        double value = *(double*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(double));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BOOLEAN
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool ReadBoolean()
    {
        bool value = *(int*)_eventRecord->UserData != 0;
        IncrementBy(ref _eventRecord->UserData, sizeof(int));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(short size)
    {
        byte[] value = new byte[size];
        Marshal.Copy((IntPtr)_eventRecord->UserData, value, 0, size);
        IncrementBy(ref _eventRecord->UserData, size);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(ushort size)
    {
        byte[] value = new byte[size];
        Marshal.Copy((IntPtr)_eventRecord->UserData, value, 0, size);
        IncrementBy(ref _eventRecord->UserData, size);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(int size)
    {
        byte[] value = new byte[size];
        Marshal.Copy((IntPtr)_eventRecord->UserData, value, 0, size);
        IncrementBy(ref _eventRecord->UserData, size);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(uint size)
    {
        byte[] value = new byte[size];
        Marshal.Copy((IntPtr)_eventRecord->UserData, value, 0, (int)size);
        IncrementBy(ref _eventRecord->UserData, (int)size);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_GUID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Guid ReadGuid()
    {
        Guid value = *(Guid*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(Guid));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_POINTER
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong ReadPointer()
    {
        if ((_eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) ==
            PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER)
        {
            return ReadUInt32();
        }

        return ReadUInt64();
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_FILETIME
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadFileTime()
    {
        return DateTime.FromFileTimeUtc(ReadInt64());
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_SYSTEMTIME
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadSystemTime()
    {
        int year = ReadInt16();
        int month = ReadInt16();
        ReadInt16();
        int day = ReadInt16();
        int hour = ReadInt16();
        int minute = ReadInt16();
        int second = ReadInt16();
        int milliseconds = ReadInt16();
        return new DateTime(year, month, day, hour, minute, second, milliseconds);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_SID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadSid()
    {
        SecurityIdentifier value = new((IntPtr)_eventRecord->UserData);
        IncrementBy(ref _eventRecord->UserData, value.BinaryLength);
        return value.Value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_COUNTEDSTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadCountedString()
    {
        ushort length = ReadUInt16();
        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_COUNTEDANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadCountedAnsiString()
    {
        ushort length = ReadUInt16();
        return ReadAnsiStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_REVERSEDCOUNTEDSTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadReversedCountedString()
    {
        byte low = ReadUInt8();
        byte high = ReadUInt8();
        ushort length = (ushort)(((uint)low & 0xFF) | (((uint)high & 0xFF) << 8));

        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_REVERSEDCOUNTEDANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadReversedCountedAnsiString()
    {
        byte low = ReadUInt8();
        byte high = ReadUInt8();
        ushort length = (ushort)(((uint)low & 0xFF) | (((uint)high & 0xFF) << 8));

        return ReadAnsiStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_NONNULLTERMINATEDSTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadNonNullTerminatedString()
    {
        return ReadUnicodeStringHelper(_eventRecord->UserDataLength);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_NONNULLTERMINATEDANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadNonNullTerminatedAnsiString()
    {
        return ReadAnsiStringHelper(_eventRecord->UserDataLength);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODECHAR
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe char ReadUnicodeChar()
    {
        char value = *(char*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, sizeof(char));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSICHAR
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe char ReadAnsiChar()
    {
        char value = (char)*(byte*)_eventRecord->UserData;
        IncrementBy(ref _eventRecord->UserData, 1);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_HEXDUMP
    ///     https://msdn.microsoft.com/en-us/library/windows/desktop/aa363800(v=vs.85).aspx
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] ReadHexDump()
    {
        return ReadBinary(ReadInt32());
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_WBEMSID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadWbemSid()
    {
        int pointerSize = (_eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) ==
                          PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER
            ? 4
            : 8;
        IncrementBy(ref _eventRecord->UserData, pointerSize * 2);
        return ReadSid();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe string ReadAnsiStringHelper(int length)
    {
        byte* ptr = (byte*)_eventRecord->UserData;
        char[] arr = new char[length];
        for (int i = 0; i < length; ++i)
        {
            arr[i] = (char)*ptr++;
        }

        IncrementBy(ref _eventRecord->UserData, length);
        return new string(arr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe string ReadUnicodeStringHelper(int length)
    {
        string value = new((char*)_eventRecord->UserData, 0, length / 2);
        IncrementBy(ref _eventRecord->UserData, value.Length * 2);
        return value;
    }
}