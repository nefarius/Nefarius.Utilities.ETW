namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     The kind of an <see cref="EtwValue" />.
/// </summary>
public enum EtwValueKind
{
    /// <summary>No value.</summary>
    Null,

    /// <summary>A <see cref="bool" />.</summary>
    Boolean,

    /// <summary>A signed integer, widened to <see cref="long" />.</summary>
    Int64,

    /// <summary>An unsigned integer or pointer, widened to <see cref="ulong" />.</summary>
    UInt64,

    /// <summary>A floating point number, widened to <see cref="double" />.</summary>
    Double,

    /// <summary>Text, including SIDs.</summary>
    String,

    /// <summary>Opaque bytes, including hex dumps.</summary>
    Binary,

    /// <summary>A <see cref="System.Guid" />.</summary>
    Guid,

    /// <summary>A UTC <see cref="System.DateTime" />.</summary>
    DateTime,

    /// <summary>An ordered list of values.</summary>
    Array,

    /// <summary>An ordered list of named values.</summary>
    Object
}

/// <summary>
///     A typed, transport-neutral value of a decoded event property.
/// </summary>
/// <remarks>
///     This is the structured counterpart of the JSON the library otherwise emits. Integers are widened to 64 bits;
///     the original ETW width is not retained.
/// </remarks>
public sealed class EtwValue
{
    private static readonly IReadOnlyList<EtwValue> NoItems = [];
    private static readonly IReadOnlyList<KeyValuePair<string, EtwValue>> NoMembers = [];

    private readonly object? _reference;
    private readonly ulong _bits;

    private EtwValue(EtwValueKind kind, ulong bits = 0, object? reference = null)
    {
        Kind = kind;
        _bits = bits;
        _reference = reference;
    }

    /// <summary>The kind of this value.</summary>
    public EtwValueKind Kind { get; }

    /// <summary>The shared null value.</summary>
    public static EtwValue Null { get; } = new(EtwValueKind.Null);

    /// <summary>The value as <see cref="bool" />.</summary>
    public bool AsBoolean => Require(EtwValueKind.Boolean) && _bits != 0;

    /// <summary>The value as <see cref="long" />.</summary>
    public long AsInt64 => Require(EtwValueKind.Int64) ? unchecked((long)_bits) : 0;

    /// <summary>The value as <see cref="ulong" />.</summary>
    public ulong AsUInt64 => Require(EtwValueKind.UInt64) ? _bits : 0;

    /// <summary>The value as <see cref="double" />.</summary>
    public double AsDouble => Require(EtwValueKind.Double) ? BitConverter.UInt64BitsToDouble(_bits) : 0;

    /// <summary>The value as <see cref="string" />.</summary>
    public string AsString => (string)Reference(EtwValueKind.String);

    /// <summary>The value as bytes.</summary>
    public byte[] AsBinary => (byte[])Reference(EtwValueKind.Binary);

    /// <summary>The value as <see cref="Guid" />.</summary>
    public Guid AsGuid => (Guid)Reference(EtwValueKind.Guid);

    /// <summary>The value as UTC <see cref="DateTime" />.</summary>
    public DateTime AsDateTime => Require(EtwValueKind.DateTime) ? new DateTime(unchecked((long)_bits), DateTimeKind.Utc) : default;

    /// <summary>The elements of an <see cref="EtwValueKind.Array" />, otherwise empty.</summary>
    public IReadOnlyList<EtwValue> Items => Kind == EtwValueKind.Array ? (IReadOnlyList<EtwValue>)_reference! : NoItems;

    /// <summary>The members of an <see cref="EtwValueKind.Object" />, otherwise empty. Names may repeat.</summary>
    public IReadOnlyList<KeyValuePair<string, EtwValue>> Members =>
        Kind == EtwValueKind.Object ? (IReadOnlyList<KeyValuePair<string, EtwValue>>)_reference! : NoMembers;

    /// <summary>Creates a boolean value.</summary>
    public static EtwValue FromBoolean(bool value) => new(EtwValueKind.Boolean, value ? 1UL : 0UL);

    /// <summary>Creates a signed integer value.</summary>
    public static EtwValue FromInt64(long value) => new(EtwValueKind.Int64, unchecked((ulong)value));

    /// <summary>Creates an unsigned integer value.</summary>
    public static EtwValue FromUInt64(ulong value) => new(EtwValueKind.UInt64, value);

    /// <summary>Creates a floating point value.</summary>
    public static EtwValue FromDouble(double value) => new(EtwValueKind.Double, BitConverter.DoubleToUInt64Bits(value));

    /// <summary>Creates a text value; <see langword="null" /> becomes <see cref="Null" />.</summary>
    public static EtwValue FromString(string? value) => value is null ? Null : new EtwValue(EtwValueKind.String, 0, value);

    /// <summary>Creates a binary value.</summary>
    public static EtwValue FromBinary(byte[] value) => new(EtwValueKind.Binary, 0, value);

    /// <summary>Creates a GUID value.</summary>
    public static EtwValue FromGuid(Guid value) => new(EtwValueKind.Guid, 0, value);

    /// <summary>Creates a date and time value.</summary>
    public static EtwValue FromDateTime(DateTime value) =>
        new(EtwValueKind.DateTime, unchecked((ulong)value.ToUniversalTime().Ticks));

    /// <summary>Creates an array value.</summary>
    public static EtwValue FromArray(IReadOnlyList<EtwValue> items) => new(EtwValueKind.Array, 0, items);

    /// <summary>Creates an object value.</summary>
    public static EtwValue FromObject(IReadOnlyList<KeyValuePair<string, EtwValue>> members) =>
        new(EtwValueKind.Object, 0, members);

    private bool Require(EtwValueKind kind)
    {
        if (Kind != kind)
        {
            throw new InvalidOperationException($"The value is {Kind}, not {kind}.");
        }

        return true;
    }

    private object Reference(EtwValueKind kind)
    {
        Require(kind);
        return _reference!;
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        EtwValueKind.Null => "null",
        EtwValueKind.Boolean => AsBoolean.ToString(),
        EtwValueKind.Int64 => AsInt64.ToString(),
        EtwValueKind.UInt64 => AsUInt64.ToString(),
        EtwValueKind.Double => AsDouble.ToString("R"),
        EtwValueKind.String => AsString,
        EtwValueKind.Binary => Convert.ToBase64String(AsBinary),
        EtwValueKind.Guid => AsGuid.ToString("D"),
        EtwValueKind.DateTime => AsDateTime.ToString("o"),
        EtwValueKind.Array => $"[{Items.Count} items]",
        _ => $"{{{Members.Count} members}}"
    };
}
