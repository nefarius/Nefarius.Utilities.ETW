using System.Linq.Expressions;
using System.Reflection;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Deserializer;

internal static class Extensions
{
    public static void AddOrUpdate<TK, TV>(this Dictionary<TK, TV> dict, TK k, TV v) where TK : notnull
    {
        if (dict.ContainsKey(k))
        {
            dict[k] = v;
        }
        else
        {
            dict.Add(k, v);
        }
    }

    public static _TDH_IN_TYPE ToTdhInType(string value)
    {
        value = value.ToLowerInvariant();
        switch (value)
        {
            case "unicodestring":
                return _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING;
            case "ansistring":
                return _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING;
            case "int8":
                return _TDH_IN_TYPE.TDH_INTYPE_INT8;
            case "uint8":
                return _TDH_IN_TYPE.TDH_INTYPE_UINT8;
            case "int16":
                return _TDH_IN_TYPE.TDH_INTYPE_INT16;
            case "uint16":
                return _TDH_IN_TYPE.TDH_INTYPE_UINT16;
            case "int32":
                return _TDH_IN_TYPE.TDH_INTYPE_INT32;
            case "uint32":
                return _TDH_IN_TYPE.TDH_INTYPE_UINT32;
            case "int64":
                return _TDH_IN_TYPE.TDH_INTYPE_INT64;
            case "uint64":
                return _TDH_IN_TYPE.TDH_INTYPE_UINT64;
            case "float":
                return _TDH_IN_TYPE.TDH_INTYPE_FLOAT;
            case "double":
                return _TDH_IN_TYPE.TDH_INTYPE_DOUBLE;
            case "boolean":
                return _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN;
            case "binary":
                return _TDH_IN_TYPE.TDH_INTYPE_BINARY;
            case "guid":
                return _TDH_IN_TYPE.TDH_INTYPE_GUID;
            case "sizet":
            case "pointer":
                return _TDH_IN_TYPE.TDH_INTYPE_POINTER;
            case "filetime":
                return _TDH_IN_TYPE.TDH_INTYPE_FILETIME;
            case "systemtime":
                return _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME;
            case "sid":
                return _TDH_IN_TYPE.TDH_INTYPE_SID;
            case "hexint32":
                return _TDH_IN_TYPE.TDH_INTYPE_HEXINT32;
            case "hexint64":
                return _TDH_IN_TYPE.TDH_INTYPE_HEXINT64;
            default:
                throw new Exception("Unreachable");
        }
    }

    /// <summary>
    ///     Maps a manifest <c>outType</c> local name (for example <c>HexInt32</c> or <c>unsignedInt</c>) to the TDH
    ///     out-type. A missing or unknown name yields <see cref="_TDH_OUT_TYPE.TDH_OUTTYPE_NULL" />, the "default
    ///     presentation" value, because the out-type is a hint and never changes how bytes are consumed.
    /// </summary>
    public static _TDH_OUT_TYPE ToTdhOutType(string? value)
    {
        return (value ?? string.Empty).ToLowerInvariant() switch
        {
            "string" => _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            "datetime" => _TDH_OUT_TYPE.TDH_OUTTYPE_DATETIME,
            "byte" => _TDH_OUT_TYPE.TDH_OUTTYPE_BYTE,
            "unsignedbyte" => _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDBYTE,
            "short" => _TDH_OUT_TYPE.TDH_OUTTYPE_SHORT,
            "unsignedshort" => _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDSHORT,
            "int" => _TDH_OUT_TYPE.TDH_OUTTYPE_INT,
            "unsignedint" => _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            "long" => _TDH_OUT_TYPE.TDH_OUTTYPE_LONG,
            "unsignedlong" => _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDLONG,
            "float" => _TDH_OUT_TYPE.TDH_OUTTYPE_FLOAT,
            "double" => _TDH_OUT_TYPE.TDH_OUTTYPE_DOUBLE,
            "boolean" => _TDH_OUT_TYPE.TDH_OUTTYPE_BOOLEAN,
            "guid" => _TDH_OUT_TYPE.TDH_OUTTYPE_GUID,
            "hexbinary" => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXBINARY,
            "hexint8" => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT8,
            "hexint16" => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT16,
            "hexint32" => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT32,
            "hexint64" => _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT64,
            "pid" => _TDH_OUT_TYPE.TDH_OUTTYPE_PID,
            "tid" => _TDH_OUT_TYPE.TDH_OUTTYPE_TID,
            "port" => _TDH_OUT_TYPE.TDH_OUTTYPE_PORT,
            "ipv4" => _TDH_OUT_TYPE.TDH_OUTTYPE_IPV4,
            "ipv6" => _TDH_OUT_TYPE.TDH_OUTTYPE_IPV6,
            "socketaddress" => _TDH_OUT_TYPE.TDH_OUTTYPE_SOCKETADDRESS,
            "cimdatetime" => _TDH_OUT_TYPE.TDH_OUTTYPE_CIMDATETIME,
            "etwtime" => _TDH_OUT_TYPE.TDH_OUTTYPE_ETWTIME,
            "xml" => _TDH_OUT_TYPE.TDH_OUTTYPE_XML,
            "errorcode" => _TDH_OUT_TYPE.TDH_OUTTYPE_ERRORCODE,
            "win32error" => _TDH_OUT_TYPE.TDH_OUTTYPE_WIN32ERROR,
            "ntstatus" => _TDH_OUT_TYPE.TDH_OUTTYPE_NTSTATUS,
            "hresult" => _TDH_OUT_TYPE.TDH_OUTTYPE_HRESULT,
            "json" => _TDH_OUT_TYPE.TDH_OUTTYPE_JSON,
            "utf8" => _TDH_OUT_TYPE.TDH_OUTTYPE_UTF8,
            _ => _TDH_OUT_TYPE.TDH_OUTTYPE_NULL
        };
    }

    public static Expression SizeOf(this _TDH_IN_TYPE tdhType)
    {
        switch (tdhType)
        {
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_INT8:
                return Expression.Constant(sizeof(sbyte));
            case _TDH_IN_TYPE.TDH_INTYPE_UINT8:
                return Expression.Constant(sizeof(byte));
            case _TDH_IN_TYPE.TDH_INTYPE_INT16:
                return Expression.Constant(sizeof(short));
            case _TDH_IN_TYPE.TDH_INTYPE_UINT16:
                return Expression.Constant(sizeof(ushort));
            case _TDH_IN_TYPE.TDH_INTYPE_INT32:
                return Expression.Constant(sizeof(int));
            case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                return Expression.Constant(sizeof(uint));
            case _TDH_IN_TYPE.TDH_INTYPE_INT64:
                return Expression.Constant(sizeof(long));
            case _TDH_IN_TYPE.TDH_INTYPE_UINT64:
                return Expression.Constant(sizeof(ulong));
            case _TDH_IN_TYPE.TDH_INTYPE_FLOAT:
                return Expression.Constant(sizeof(float));
            case _TDH_IN_TYPE.TDH_INTYPE_DOUBLE:
                return Expression.Constant(sizeof(double));
            case _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN:
                return Expression.Constant(4); // this is because in Windows we have bools as ints
            case _TDH_IN_TYPE.TDH_INTYPE_BINARY:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                return Expression.Constant(16); // otherwise requires unsafe
            case _TDH_IN_TYPE.TDH_INTYPE_POINTER:
                return Expression.Constant(4); // TODO: FIXME with conditional
            case _TDH_IN_TYPE.TDH_INTYPE_FILETIME:
                return Expression.Constant(16); // Windows
            case _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME:
                return Expression.Constant(16); // Windows
            case _TDH_IN_TYPE.TDH_INTYPE_SID:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT32:
                return Expression.Constant(sizeof(int));
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT64:
                return Expression.Constant(sizeof(long));
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDSTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDANSISTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDSTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDANSISTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDSTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDANSISTRING:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODECHAR:
                return Expression.Constant(sizeof(char));
            case _TDH_IN_TYPE.TDH_INTYPE_ANSICHAR:
                return Expression.Constant(sizeof(byte));
            case _TDH_IN_TYPE.TDH_INTYPE_SIZET:
                return Expression.Constant(4); // TODO: FIXME with conditional
            case _TDH_IN_TYPE.TDH_INTYPE_HEXDUMP:
                return Expression.Constant(0); // size check happens inside his reader
            case _TDH_IN_TYPE.TDH_INTYPE_WBEMSID:
                return Expression.Constant(0); // size check happens inside his reader
            default:
                throw new Exception("Unreachable");
        }
    }

    public static Type CSharpType(this _TDH_IN_TYPE tdhType)
    {
        switch (tdhType)
        {
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_INT8:
                return typeof(sbyte);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT8:
                return typeof(byte);
            case _TDH_IN_TYPE.TDH_INTYPE_INT16:
                return typeof(short);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT16:
                return typeof(ushort);
            case _TDH_IN_TYPE.TDH_INTYPE_INT32:
                return typeof(int);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                return typeof(uint);
            case _TDH_IN_TYPE.TDH_INTYPE_INT64:
                return typeof(long);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT64:
                return typeof(ulong);
            case _TDH_IN_TYPE.TDH_INTYPE_FLOAT:
                return typeof(float);
            case _TDH_IN_TYPE.TDH_INTYPE_DOUBLE:
                return typeof(double);
            case _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN:
                return typeof(bool);
            case _TDH_IN_TYPE.TDH_INTYPE_BINARY:
                return typeof(byte[]);
            case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                return typeof(Guid);
            case _TDH_IN_TYPE.TDH_INTYPE_POINTER:
                return typeof(ulong);
            case _TDH_IN_TYPE.TDH_INTYPE_FILETIME:
                return typeof(DateTime);
            case _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME:
                return typeof(DateTime);
            case _TDH_IN_TYPE.TDH_INTYPE_SID:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT32:
                return typeof(int);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT64:
                return typeof(long);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDSTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDANSISTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDSTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDANSISTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDSTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDANSISTRING:
                return typeof(string);
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODECHAR:
                return typeof(char);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSICHAR:
                return typeof(char);
            case _TDH_IN_TYPE.TDH_INTYPE_SIZET:
                return typeof(ulong);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXDUMP:
                return typeof(byte[]);
            case _TDH_IN_TYPE.TDH_INTYPE_WBEMSID:
                return typeof(string);
            default:
                throw new Exception("Unreachable");
        }
    }

    public static MethodInfo ReadMethodInfo(this _TDH_IN_TYPE tdhType, Type type, params Type[] parameterType)
    {
        switch (tdhType)
        {
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING:
                return GetMethodInfo(type, "ReadUnicodeString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING:
                return GetMethodInfo(type, "ReadAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT8:
                return GetMethodInfo(type, "ReadInt8", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT8:
                return GetMethodInfo(type, "ReadUInt8", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT16:
                return GetMethodInfo(type, "ReadInt16", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT16:
                return GetMethodInfo(type, "ReadUInt16", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT32:
                return GetMethodInfo(type, "ReadInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                return GetMethodInfo(type, "ReadUInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT64:
                return GetMethodInfo(type, "ReadInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT64:
                return GetMethodInfo(type, "ReadUInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_FLOAT:
                return GetMethodInfo(type, "ReadFloat", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_DOUBLE:
                return GetMethodInfo(type, "ReadDouble", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN:
                return GetMethodInfo(type, "ReadBoolean", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_BINARY:
                return GetMethodInfo(type, "ReadBinary", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                return GetMethodInfo(type, "ReadGuid", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_POINTER:
                return GetMethodInfo(type, "ReadPointer", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_FILETIME:
                return GetMethodInfo(type, "ReadFileTime", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME:
                return GetMethodInfo(type, "ReadSystemTime", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SID:
                return GetMethodInfo(type, "ReadSid", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT32:
                return GetMethodInfo(type, "ReadInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT64:
                return GetMethodInfo(type, "ReadInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDSTRING:
                return GetMethodInfo(type, "ReadCountedString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDANSISTRING:
                return GetMethodInfo(type, "ReadCountedAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDSTRING:
                return GetMethodInfo(type, "ReadReversedCountedString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDANSISTRING:
                return GetMethodInfo(type, "ReadReversedCountedAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDSTRING:
                return GetMethodInfo(type, "ReadNonNullTerminatedString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDANSISTRING:
                return GetMethodInfo(type, "ReadNonNullTerminatedAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODECHAR:
                return GetMethodInfo(type, "ReadUnicodeChar", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSICHAR:
                return GetMethodInfo(type, "ReadAnsiChar", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SIZET:
                return GetMethodInfo(type, "ReadPointer", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXDUMP:
                return GetMethodInfo(type, "ReadHexDump", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_WBEMSID:
                return GetMethodInfo(type, "ReadWbemSid", parameterType);
            default:
                throw new Exception("Unreachable");
        }
    }

    public static MethodInfo WriteMethodInfo(this _TDH_IN_TYPE tdhType, Type type, params Type[] parameterType)
    {
        switch (tdhType)
        {
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING:
                return GetMethodInfo(type, "WriteUnicodeString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSISTRING:
                return GetMethodInfo(type, "WriteAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT8:
                return GetMethodInfo(type, "WriteInt8", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT8:
                return GetMethodInfo(type, "WriteUInt8", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT16:
                return GetMethodInfo(type, "WriteInt16", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT16:
                return GetMethodInfo(type, "WriteUInt16", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT32:
                return GetMethodInfo(type, "WriteInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                return GetMethodInfo(type, "WriteUInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_INT64:
                return GetMethodInfo(type, "WriteInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UINT64:
                return GetMethodInfo(type, "WriteUInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_FLOAT:
                return GetMethodInfo(type, "WriteFloat", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_DOUBLE:
                return GetMethodInfo(type, "WriteDouble", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_BOOLEAN:
                return GetMethodInfo(type, "WriteBoolean", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_BINARY:
                return GetMethodInfo(type, "WriteBinary", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                return GetMethodInfo(type, "WriteGuid", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_POINTER:
                return GetMethodInfo(type, "WritePointer", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_FILETIME:
                return GetMethodInfo(type, "WriteFileTime", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME:
                return GetMethodInfo(type, "WriteSystemTime", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SID:
                return GetMethodInfo(type, "WriteSid", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT32:
                return GetMethodInfo(type, "WriteInt32", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXINT64:
                return GetMethodInfo(type, "WriteInt64", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDSTRING:
                return GetMethodInfo(type, "WriteUnicodeString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_COUNTEDANSISTRING:
                return GetMethodInfo(type, "WriteAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDSTRING:
                return GetMethodInfo(type, "WriteUnicodeString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_REVERSEDCOUNTEDANSISTRING:
                return GetMethodInfo(type, "WriteAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDSTRING:
                return GetMethodInfo(type, "WriteUnicodeString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_NONNULLTERMINATEDANSISTRING:
                return GetMethodInfo(type, "WriteAnsiString", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_UNICODECHAR:
                return GetMethodInfo(type, "WriteUnicodeChar", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_ANSICHAR:
                return GetMethodInfo(type, "WriteAnsiChar", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_SIZET:
                return GetMethodInfo(type, "WritePointer", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_HEXDUMP:
                return GetMethodInfo(type, "WriteBinary", parameterType);
            case _TDH_IN_TYPE.TDH_INTYPE_WBEMSID:
                return GetMethodInfo(type, "WriteSid", parameterType);
            default:
                throw new Exception("Unreachable");
        }
    }

    private static MethodInfo GetMethodInfo(Type type, string name, params Type[] parameterType)
    {
        return type.GetMethod(name, parameterType)
            ?? throw new MissingMethodException(type.FullName, name);
    }
}