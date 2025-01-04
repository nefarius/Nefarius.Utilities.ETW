namespace Nefarius.Utilities.ETW.Deserializer;

internal readonly struct TraceEventKey : IEquatable<TraceEventKey>
{
    private readonly Guid _providerId;

    private readonly ushort _id;

    private readonly byte _version;

    public TraceEventKey(Guid providerId, ushort id, byte version)
    {
        _providerId = providerId;
        _id = id;
        _version = version;
    }

    public bool Equals(TraceEventKey other)
    {
        return _providerId.Equals(other._providerId) && _id == other._id && _version == other._version;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        return obj is TraceEventKey && Equals((TraceEventKey)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = _providerId.GetHashCode();
            hashCode = (hashCode * 397) ^ _id.GetHashCode();
            hashCode = (hashCode * 397) ^ _version.GetHashCode();
            return hashCode;
        }
    }
}