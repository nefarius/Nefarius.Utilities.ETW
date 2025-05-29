namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Decoding information for WPP events.
/// </summary>
public sealed class TraceMessageFormat : IEquatable<TraceMessageFormat>, IComparable<TraceMessageFormat>, IComparable
{
    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (ReferenceEquals(this, obj))
        {
            return 0;
        }

        return obj is TraceMessageFormat other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(TraceMessageFormat)}");
    }

    public int CompareTo(TraceMessageFormat? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1;
        }

        int messageGuidComparison = MessageGuid.CompareTo(other.MessageGuid);
        if (messageGuidComparison != 0)
        {
            return messageGuidComparison;
        }

        int idComparison = Id.CompareTo(other.Id);
        if (idComparison != 0)
        {
            return idComparison;
        }

        return string.Compare(Function, other.Function, StringComparison.InvariantCulture);
    }

    public bool Equals(TraceMessageFormat? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return MessageGuid.Equals(other.MessageGuid) &&
               Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || (obj is TraceMessageFormat other && Equals(other));
    }

    public override int GetHashCode()
    {
        HashCode hashCode = new();
        hashCode.Add(MessageGuid);
        hashCode.Add(Id);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(TraceMessageFormat? left, TraceMessageFormat? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(TraceMessageFormat? left, TraceMessageFormat? right)
    {
        return !Equals(left, right);
    }

    #region Base TMF essential properties

    public Guid MessageGuid { get; init; }

    public required string Provider { get; init; }

    public required string FileName { get; init; }

    public required string Opcode { get; init; }

    public int Id { get; init; }

    public required string MessageFormat { get; init; }

    public required string Level { get; init; }

    public required string Flags { get; init; }

    public required string Function { get; init; }

    public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; } = new List<FunctionParameter>();

    #endregion
}