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

        /// <summary>Extra HTTP headers for the downstream receiver, e.g. an API key.</summary>
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
