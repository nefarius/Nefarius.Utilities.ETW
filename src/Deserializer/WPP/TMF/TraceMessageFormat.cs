namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public sealed class TraceMessageFormat : IEquatable<TraceMessageFormat>, IComparable<TraceMessageFormat>, IComparable
{
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

        int providerComparison = string.Compare(Provider, other.Provider, StringComparison.InvariantCulture);
        if (providerComparison != 0)
        {
            return providerComparison;
        }

        int fileNameComparison = string.Compare(FileName, other.FileName, StringComparison.InvariantCulture);
        if (fileNameComparison != 0)
        {
            return fileNameComparison;
        }

        int opcodeComparison = string.Compare(Opcode, other.Opcode, StringComparison.InvariantCulture);
        if (opcodeComparison != 0)
        {
            return opcodeComparison;
        }

        int idComparison = Id.CompareTo(other.Id);
        if (idComparison != 0)
        {
            return idComparison;
        }

        int messageFormatComparison =
            string.Compare(MessageFormat, other.MessageFormat, StringComparison.InvariantCulture);
        if (messageFormatComparison != 0)
        {
            return messageFormatComparison;
        }

        int levelComparison = string.Compare(Level, other.Level, StringComparison.InvariantCulture);
        if (levelComparison != 0)
        {
            return levelComparison;
        }

        int flagsComparison = string.Compare(Flags, other.Flags, StringComparison.InvariantCulture);
        if (flagsComparison != 0)
        {
            return flagsComparison;
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
               string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(FileName, other.FileName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Opcode, other.Opcode, StringComparison.OrdinalIgnoreCase) &&
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
        hashCode.Add(Provider, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(FileName, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(Opcode, StringComparer.OrdinalIgnoreCase);
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
}