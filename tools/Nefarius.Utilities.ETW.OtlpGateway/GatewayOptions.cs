namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Configuration of the decoding gateway, bound from the <c>Gateway</c> configuration section.
/// </summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Where decoded log records go.</summary>
    public ExportOptions Export { get; set; } = new();

    /// <summary>Server-side WPP decoding assets. They never leave this process.</summary>
    public WppOptions Wpp { get; set; } = new();

    /// <summary>Limits for incoming requests and sessions.</summary>
    public LimitOptions Limits { get; set; } = new();

    /// <summary>
    ///     When true, the OTLP endpoints require an authenticated user, using whatever authentication the host
    ///     registered. When false the gateway performs no authorization of its own and is expected to run behind an
    ///     authenticating proxy or Collector.
    /// </summary>
    public bool RequireAuthorization { get; set; }

    public sealed class ExportOptions
    {
        /// <summary>
        ///     Base URL of the downstream OTLP/HTTP receiver, e.g. <c>http://collector:4318</c>. The gateway posts to
        ///     <c>{Endpoint}/v1/logs</c>. Empty discards decoded records, which is only useful for testing.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        ///     Base URL of an OTLP/HTTP receiver for <b>traces</b>, e.g. <c>http://jaeger:4318</c>. When set, every decoded
        ///     event is also exported as a span (one trace per tenant and relay session), so trace-only backends such as
        ///     Jaeger can show it. The same <see cref="Headers" /> sent to <see cref="Endpoint" />, including any
        ///     credentials, are sent here too.
        /// </summary>
        public string TracesEndpoint { get; set; } = string.Empty;

        /// <summary>
        ///     Extra HTTP headers for both downstream receivers, e.g. an API key. The same headers, including any
        ///     credentials, are sent to <see cref="Endpoint" /> and to <see cref="TracesEndpoint" />.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = [];

        /// <summary>Maximum decoded batches waiting for export before senders get a retryable error.</summary>
        public int QueueCapacity { get; set; } = 1024;

        /// <summary>Attempts per batch before it is dropped.</summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>First retry delay; doubled per attempt up to <see cref="MaxRetryDelay" />.</summary>
        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Upper bound of the retry delay.</summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Timeout of one export request.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public sealed class WppOptions
    {
        /// <summary>PDB files containing WPP trace message formats.</summary>
        public List<string> PdbFiles { get; set; } = [];

        /// <summary>Directories searched (non-recursively) for <c>*.pdb</c> files.</summary>
        public List<string> PdbDirectories { get; set; } = [];

        /// <summary>Directories containing <c>*.tmf</c> files.</summary>
        public List<string> TmfDirectories { get; set; } = [];

        /// <summary>Replace the WPP provider name with the friendly control GUID name where unambiguous.</summary>
        public bool RewriteProviderName { get; set; }

        /// <summary>
        ///     Optional symbol server base URL (e.g. <c>https://msdl.microsoft.com/download/symbols</c>), operator
        ///     configuration only. When set, PDB identities (name, GUID, age) found in relayed
        ///     <c>DbgID_RSDS</c> events are downloaded from here before the request is decoded. Redirects are not followed.
        /// </summary>
        public string SymbolServer { get; set; } = string.Empty;

        /// <summary>Directory caching downloaded PDBs. Empty uses a folder below the temp directory.</summary>
        public string SymbolCache { get; set; } = string.Empty;

        /// <summary>Timeout of one symbol server request.</summary>
        public TimeSpan SymbolRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     How long a request waits in memory for symbols that are still resolving before it is decoded with
        ///     placeholders. Resolution continues in the background either way.
        /// </summary>
        public TimeSpan SymbolWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Requests that may wait for symbols at once; further requests are decoded without waiting.</summary>
        public int SymbolMaxQueuedRequests { get; set; } = 32;

        /// <summary>Download attempts per symbol for transient failures (timeouts, 5xx, network errors).</summary>
        public int SymbolMaxAttempts { get; set; } = 3;
    }

    public sealed class LimitOptions
    {
        /// <summary>Maximum size of an OTLP request body after decompression.</summary>
        public int MaxRequestBodyBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>Maximum relay frame size.</summary>
        public int MaxFrameBytes { get; set; } = 1024 * 1024;

        public int MaxSessions { get; set; } = 1024;

        public int MaxSessionsPerTenant { get; set; } = 32;

        public int MaxRecordsPerRequest { get; set; } = 10_000;

        public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);
    }
}
