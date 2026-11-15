namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>How frames arrive on the input stream.</summary>
internal enum InputFormat
{
    /// <summary>Concatenated binary frames. The default.</summary>
    Binary,

    /// <summary>One <c>{"frame":"&lt;base64&gt;"}</c> JSON object per line.</summary>
    Base64Ndjson
}

/// <summary>The command line was invalid (exit code 2).</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>The input stream is not a valid relay stream (exit code 2).</summary>
internal sealed class InputException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Delivery to the gateway failed for good (exit code 1).</summary>
internal sealed class DeliveryException(string message) : Exception(message);

/// <summary>
///     Everything <c>etwrelay</c> needs to run, validated.
/// </summary>
internal sealed class RelaySettings
{
    public const int MaxFramesPerBatch = 64;

    /// <summary>The full <c>/v1/logs</c> URL.</summary>
    public required Uri Endpoint { get; init; }

    public InputFormat InputFormat { get; init; } = InputFormat.Binary;

    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    public IReadOnlyList<KeyValuePair<string, string>> Resources { get; init; } = [];

    public bool Gzip { get; init; }

    /// <summary>Frames per request, at most <see cref="MaxFramesPerBatch" />, the gateway's duplicate-retry window.</summary>
    public int BatchFrames { get; init; } = MaxFramesPerBatch;

    /// <summary>A batch is sent once its frames add up to this many bytes.</summary>
    public int BatchBytes { get; init; } = 1024 * 1024;

    /// <summary>The longest a frame waits in an incomplete batch.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Frames buffered between the input reader and the sender. A full queue stalls the producer.</summary>
    public int QueueFrames { get; init; } = 1024;

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries after the first attempt before delivery is abandoned.</summary>
    public int MaxRetries { get; init; } = 8;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Longest wait a <c>Retry-After</c> header can impose.</summary>
    public TimeSpan RetryAfterCap { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Largest frame accepted from the input.</summary>
    public int MaxFrameBytes { get; init; } = 1024 * 1024;

    /// <summary>
    ///     Validates an endpoint and turns it into the logs URL. An <c>http</c> endpoint is accepted only when no header
    ///     looks like a credential.
    /// </summary>
    public static Uri BuildEndpoint(string endpoint, IEnumerable<KeyValuePair<string, string>> headers)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            throw new UsageException($"--endpoint '{endpoint}' is not an absolute http(s) URL.");
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new UsageException("--endpoint must not embed credentials; use --header over https.");
        }

        // A query is kept for https endpoints. On http it would travel in clear text, and names like
        // "api_key" are not covered by the header check, so any query is refused there.
        if (baseUri.Scheme == "http" && !string.IsNullOrEmpty(baseUri.Query) && baseUri.Query != "?")
        {
            throw new UsageException(
                "--endpoint cannot carry a query string over http://; it would be sent in clear text. Use https://.");
        }

        if (baseUri.Scheme == "http")
        {
            string? credential = headers.Select(h => h.Key).FirstOrDefault(IsCredentialHeader);
            if (credential is not null)
            {
                throw new UsageException(
                    $"The credential-like header '{credential}' cannot be sent to an http:// endpoint. Use https://.");
            }
        }

        UriBuilder builder = new(baseUri);
        string path = builder.Path.TrimEnd('/');
        builder.Path = path.EndsWith("/v1/logs", StringComparison.OrdinalIgnoreCase) ? path : path + "/v1/logs";
        return builder.Uri;
    }

    public static bool IsCredentialHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits <c>name=value</c> pairs. Names must be unique and non-empty.</summary>
    public static List<KeyValuePair<string, string>> ParsePairs(IEnumerable<string> raw, string option)
    {
        List<KeyValuePair<string, string>> pairs = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string item in raw)
        {
            int eq = item.IndexOf('=');
            if (eq <= 0)
            {
                throw new UsageException($"{option} expects name=value, got '{item}'.");
            }

            string name = item[..eq].Trim();
            if (name.Length == 0)
            {
                throw new UsageException($"{option} expects name=value, got '{item}'.");
            }

            if (item.Any(c => c is (< ' ' and not '\t') or '\u007f'))
            {
                throw new UsageException($"{option} '{name}' contains a control character.");
            }

            if (!seen.Add(name))
            {
                throw new UsageException($"{option} '{name}' is given more than once.");
            }

            pairs.Add(new KeyValuePair<string, string>(name, item[(eq + 1)..]));
        }

        return pairs;
    }
}
