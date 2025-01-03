namespace Nefarius.Utilities.ETW.Deserializer
{
    internal readonly struct TraceEventKey : IEquatable<TraceEventKey>
    {
        private readonly Guid _providerId;

        private readonly ushort _id;

        private readonly byte _version;

        public TraceEventKey(Guid providerId, ushort id, byte version)
        {
            this._providerId = providerId;
            this._id = id;
            this._version = version;
        }

        public bool Equals(TraceEventKey other)
        {
            return this._providerId.Equals(other._providerId) && this._id == other._id && this._version == other._version;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is TraceEventKey && this.Equals((TraceEventKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = this._providerId.GetHashCode();
                hashCode = (hashCode * 397) ^ this._id.GetHashCode();
                hashCode = (hashCode * 397) ^ this._version.GetHashCode();
                return hashCode;
            }
        }
    }
}