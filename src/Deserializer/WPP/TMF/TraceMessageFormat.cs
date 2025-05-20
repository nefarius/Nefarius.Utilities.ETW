namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public sealed class TraceMessageFormat : IEquatable<TraceMessageFormat>
{
    public Guid MessageGuid { get; init; }

    public required string Provider { get; init; }

    public required string FileName { get; init; }

    public required string Opcode { get; init; }

    public int Id { get; init; }

    public required string MessageFormat { get; init; }

    public required string Level { get; init; }

    public required string Flags { get; init; }

    public string? Function { get; init; }

    public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; } = new List<FunctionParameter>();

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
               string.Equals(Opcode, other.Opcode, StringComparison.OrdinalIgnoreCase) && Id == other.Id;
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