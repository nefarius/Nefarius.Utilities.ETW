using System.Text;

using Microsoft.Xml.Serialization.GeneratedAssembly;

namespace Nefarius.Utilities.ETW.Deserializer
{
    internal sealed class EventSourceManifest
    {
        private readonly StringBuilder chunkBuilder = new StringBuilder();

        private readonly Guid providerGuid;

        private readonly byte format;

        private readonly byte majorVersion;

        private readonly byte minorVersion;

        private readonly byte magic;

        private readonly ushort totalChunks;

        private ushort chunksReceived;

        private instrumentationManifest? manifest;

        public EventSourceManifest(Guid providerGuid, byte format, byte majorVersion, byte minorVersion, byte magic, ushort totalChunks)
        {
            this.providerGuid = providerGuid;
            this.format = format;
            this.majorVersion = majorVersion;
            this.minorVersion = minorVersion;
            this.magic = magic;
            this.totalChunks = totalChunks;
        }

        public EventSourceManifest(Guid providerGuid, Stream source)
        {
            this.providerGuid = providerGuid;
            instrumentationManifestSerializer serializer = new();
            this.manifest = serializer.Deserialize(source) as instrumentationManifest
                ?? throw new InvalidDataException("The manifest stream is not an instrumentation manifest.");
            source.Dispose();
        }

        public bool IsComplete => this.chunksReceived == this.totalChunks;

        public instrumentationManifest Schema
        {
            get
            {
                if (this.chunksReceived != this.totalChunks)
                {
                    throw new InvalidOperationException("Schema is incomplete as not all chunks have been received");
                }

                if (this.manifest is instrumentationManifest existing)
                {
                    return existing;
                }

                string value = this.chunkBuilder.ToString();
                byte[] bytes = Encoding.ASCII.GetBytes(value);
                using MemoryStream ms = new(bytes);
                instrumentationManifestSerializer serializer = new();
                instrumentationManifest schema = serializer.Deserialize(ms) as instrumentationManifest
                    ?? throw new InvalidDataException("The manifest stream is not an instrumentation manifest.");
                this.manifest = schema;
                return schema;
            }
        }

        public Guid ProviderGuid => this.providerGuid;

        public byte Format => this.format;

        public byte MajorVersion => this.majorVersion;

        public byte MinorVersion => this.minorVersion;

        public byte Magic => this.magic;

        public ushort TotalChunks => this.totalChunks;

        public void AddChunk(string schemaChunk)
        {
            this.chunkBuilder.Append(schemaChunk);
            ++this.chunksReceived;
        }

        public override bool Equals(object obj)
        {
            var other = obj as EventSourceManifest;
            return other != null && this.Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = this.providerGuid.GetHashCode();
                hashCode = (hashCode * 397) ^ this.format.GetHashCode();
                hashCode = (hashCode * 397) ^ this.majorVersion.GetHashCode();
                hashCode = (hashCode * 397) ^ this.minorVersion.GetHashCode();
                hashCode = (hashCode * 397) ^ this.magic.GetHashCode();
                hashCode = (hashCode * 397) ^ this.totalChunks.GetHashCode();

                return hashCode;
            }
        }

        public bool Equals(EventSourceManifest other)
        {
            return this.providerGuid.Equals(other.providerGuid)
                   && this.format == other.format
                   && this.majorVersion == other.majorVersion
                   && this.minorVersion == other.minorVersion
                   && this.magic == other.magic
                   && this.totalChunks == other.totalChunks;
        }
    }
}