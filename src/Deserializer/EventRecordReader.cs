using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Nefarius.Utilities.ETW.Deserializer;

internal readonly struct EventRecordReader
{
    internal unsafe EVENT_RECORD* NativeEventRecord { get; }

    private readonly IntPtr _originalUserData;

    internal unsafe EventRecordReader(EVENT_RECORD* eventRecord)
    {
        NativeEventRecord = eventRecord;

        _originalUserData = (IntPtr)eventRecord->UserData;
    }

    /// <summary>
    ///     Number of payload bytes not consumed yet.
    /// </summary>
    public unsafe int RemainingBytes =>
        Math.Max(0, NativeEventRecord->UserDataLength - (int)((byte*)NativeEventRecord->UserData - (byte*)_originalUserData));

    /// <summary>
    ///     Number of payload bytes consumed so far; may exceed the payload length after an overrun.
    /// </summary>
    public unsafe int ConsumedBytes => (int)((byte*)NativeEventRecord->UserData - (byte*)_originalUserData);

    private unsafe void IncrementBy(ref void* ptr, int value)
    {
        ptr = (byte*)ptr + value;
    }

    /// <summary>
    ///     Resets the read pointer to the beginning of the event.
    /// </summary>
    public unsafe void Reset()
    {
        NativeEventRecord->UserData = (void*)_originalUserData;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODESTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadUnicodeString()
    {
        int length = 0;
        byte* ptr = (byte*)NativeEventRecord->UserData;
        long maxLength = RemainingBytes;

        // A terminator is a UTF-16 code unit. Stepping one byte at a time treats the
        // 00 00 formed by 'A' (41 00) and U+0100 (00 01) as the end of the string.
        while (length + 1 < maxLength && (ptr[length] != 0 || ptr[length + 1] != 0))
        {
            length += 2;
        }

        bool terminated = length + 1 < maxLength;
        string value = new((char*)NativeEventRecord->UserData, 0, length / 2);
        IncrementBy(ref NativeEventRecord->UserData, length + (terminated ? 2 : 0));
        return value;
    }

    /// <summary>
    ///     A TraceLogging Unicode string. The payload must contain the terminator.
    /// </summary>
    public unsafe string ReadTerminatedUnicodeString()
    {
        int length = 0;
        byte* ptr = (byte*)NativeEventRecord->UserData;
        int maxLength = RemainingBytes;
        while (length + 1 < maxLength && (ptr[length] != 0 || ptr[length + 1] != 0))
        {
            length += 2;
        }

        if (length + 1 >= maxLength)
        {
            throw new FormatException("Unicode string is not terminated.");
        }

        string value = new((char*)NativeEventRecord->UserData, 0, length / 2);
        IncrementBy(ref NativeEventRecord->UserData, length + 2);
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
        byte* ptr = (byte*)NativeEventRecord->UserData;
        long maxLength = RemainingBytes;
        while (length < maxLength && ptr[length] != 0)
        {
            ++length;
        }

        char[] arr = new char[length];
        for (int i = 0; i < length; ++i)
        {
            arr[i] = (char)*ptr++;
        }

        bool terminated = length < maxLength;
        IncrementBy(ref NativeEventRecord->UserData, length + (terminated ? 1 : 0));
        return new string(arr);
    }

    /// <summary>
    ///     A TraceLogging ANSI string. The payload must contain the terminator.
    /// </summary>
    public unsafe string ReadTerminatedAnsiString()
    {
        int length = 0;
        byte* ptr = (byte*)NativeEventRecord->UserData;
        int maxLength = RemainingBytes;
        while (length < maxLength && ptr[length] != 0)
        {
            length++;
        }

        if (length >= maxLength)
        {
            throw new FormatException("ANSI string is not terminated.");
        }

        char[] arr = new char[length];
        for (int i = 0; i < length; i++)
        {
            arr[i] = (char)ptr[i];
        }

        IncrementBy(ref NativeEventRecord->UserData, length + 1);
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
        Need(sizeof(sbyte));
        sbyte value = *(sbyte*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(sbyte));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT8
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte ReadUInt8()
    {
        Need(sizeof(byte));
        byte value = *(byte*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(byte));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT16
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe short ReadInt16()
    {
        Need(sizeof(short));
        short value = *(short*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(short));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT16
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ushort ReadUInt16()
    {
        Need(sizeof(ushort));
        ushort value = *(ushort*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(ushort));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT32
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int ReadInt32()
    {
        Need(sizeof(int));
        int value = *(int*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(int));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT32
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe uint ReadUInt32()
    {
        Need(sizeof(uint));
        uint value = *(uint*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(uint));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_INT64
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long ReadInt64()
    {
        Need(sizeof(long));
        long value = *(long*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(long));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UINT64
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong ReadUInt64()
    {
        Need(sizeof(ulong));
        ulong value = *(ulong*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(ulong));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_FLOAT
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float ReadFloat()
    {
        Need(sizeof(float));
        float value = *(float*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(float));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_DOUBLE
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe double ReadDouble()
    {
        Need(sizeof(double));
        double value = *(double*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(double));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BOOLEAN
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool ReadBoolean()
    {
        Need(sizeof(int));
        bool value = *(int*)NativeEventRecord->UserData != 0;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(int));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(short size)
    {
        return CopyBytes(size);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(ushort size)
    {
        return CopyBytes(size);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(int size)
    {
        return CopyBytes(size);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_BINARY
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe byte[] ReadBinary(uint size)
    {
        if (size > int.MaxValue)
        {
            throw new FormatException("Binary field is longer than the payload.");
        }

        return CopyBytes((int)size);
    }

    /// <summary>
    ///     Throws when fewer than <paramref name="bytes" /> remain, so a short payload cannot be read past its end.
    /// </summary>
    private unsafe void Need(int bytes)
    {
        if (RemainingBytes < bytes)
        {
            throw new FormatException("Event payload is shorter than its schema.");
        }
    }

    private unsafe byte[] CopyBytes(int size)
    {
        if (size < 0 || size > RemainingBytes)
        {
            throw new FormatException("Binary field is longer than the payload.");
        }

        byte[] value = new byte[size];
        if (size > 0)
        {
            Marshal.Copy((IntPtr)NativeEventRecord->UserData, value, 0, size);
        }

        IncrementBy(ref NativeEventRecord->UserData, size);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_GUID
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Guid ReadGuid()
    {
        Need(sizeof(Guid));
        Guid value = *(Guid*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(Guid));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_POINTER
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ulong ReadPointer()
    {
        if ((NativeEventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) ==
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
        if (RemainingBytes < 8)
        {
            throw new FormatException("SID is truncated.");
        }

        byte* p = (byte*)NativeEventRecord->UserData;
        byte revision = p[0];
        int count = p[1];
        int size = 8 + count * 4;
        if (count > 15 || size > RemainingBytes)
        {
            throw new FormatException("SID is truncated.");
        }

        ulong authority = 0;
        for (int i = 0; i < 6; i++)
        {
            authority = (authority << 8) | p[2 + i];
        }

        StringBuilder text = new();
        text.Append('S').Append('-').Append(revision).Append('-').Append(authority);
        for (int i = 0; i < count; i++)
        {
            text.Append('-').Append(*(uint*)(p + 8 + 4 * i));
        }

        IncrementBy(ref NativeEventRecord->UserData, size);
        return text.ToString();
    }

    /// <summary>
    ///     Reader for a NUL-terminated UTF-8 string (TDH out type 35).
    /// </summary>
    public unsafe string ReadUtf8String()
    {
        byte* ptr = (byte*)NativeEventRecord->UserData;
        int maxLength = RemainingBytes;
        int length = 0;
        while (length < maxLength && ptr[length] != 0)
        {
            length++;
        }

        if (length >= maxLength)
        {
            throw new FormatException("UTF-8 string is not terminated.");
        }

        string value = Encoding.UTF8.GetString(ptr, length);
        IncrementBy(ref NativeEventRecord->UserData, length + 1);
        return value;
    }

    /// <summary>
    ///     Reader for a counted UTF-8 string (TDH out type 35).
    /// </summary>
    public unsafe string ReadCountedUtf8String()
    {
        if (RemainingBytes < sizeof(ushort))
        {
            throw new FormatException("UTF-8 string is truncated.");
        }

        int length = ReadUInt16();
        if (length > RemainingBytes)
        {
            throw new FormatException("UTF-8 string is truncated.");
        }

        string value = Encoding.UTF8.GetString((byte*)NativeEventRecord->UserData, length);
        IncrementBy(ref NativeEventRecord->UserData, length);
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_COUNTEDSTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadCountedString()
    {
        if (RemainingBytes < sizeof(ushort))
        {
            throw new FormatException("Counted string is truncated.");
        }

        ushort length = ReadUInt16();
        if ((length & 1) != 0 || length > RemainingBytes)
        {
            throw new FormatException("Counted string is truncated.");
        }

        return ReadUnicodeStringHelper(length);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_COUNTEDANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadCountedAnsiString()
    {
        if (RemainingBytes < sizeof(ushort))
        {
            throw new FormatException("Counted string is truncated.");
        }

        ushort length = ReadUInt16();
        if (length > RemainingBytes)
        {
            throw new FormatException("Counted string is truncated.");
        }

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
        if ((length & 1) != 0 || length > RemainingBytes)
        {
            throw new FormatException("Counted string is truncated.");
        }

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
        return ReadUnicodeStringHelper(RemainingBytes);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_NONNULLTERMINATEDANSISTRING
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe string ReadNonNullTerminatedAnsiString()
    {
        return ReadAnsiStringHelper(RemainingBytes);
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_UNICODECHAR
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe char ReadUnicodeChar()
    {
        char value = *(char*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, sizeof(char));
        return value;
    }

    /// <summary>
    ///     Reader for TDH_INTYPE_ANSICHAR
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe char ReadAnsiChar()
    {
        char value = (char)*(byte*)NativeEventRecord->UserData;
        IncrementBy(ref NativeEventRecord->UserData, 1);
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
        int pointerSize = (NativeEventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) ==
                          PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER
            ? 4
            : 8;
        IncrementBy(ref NativeEventRecord->UserData, pointerSize * 2);
        return ReadSid();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe string ReadAnsiStringHelper(int length)
    {
        if (length < 0 || length > RemainingBytes)
        {
            throw new FormatException("String is longer than the payload.");
        }

        byte* ptr = (byte*)NativeEventRecord->UserData;
        char[] arr = new char[length];
        for (int i = 0; i < length; ++i)
        {
            arr[i] = (char)*ptr++;
        }

        IncrementBy(ref NativeEventRecord->UserData, length);
        return new string(arr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe string ReadUnicodeStringHelper(int length)
    {
        if (length < 0 || length > RemainingBytes)
        {
            throw new FormatException("String is longer than the payload.");
        }

        string value = new((char*)NativeEventRecord->UserData, 0, length / 2);
        IncrementBy(ref NativeEventRecord->UserData, value.Length * 2);
        return value;
    }
}