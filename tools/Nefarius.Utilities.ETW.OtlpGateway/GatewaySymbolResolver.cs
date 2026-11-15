using System.Collections.Concurrent;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;
using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.Relay;
using Nefarius.Utilities.ETW.Symbols;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Discovers PDB identities in relayed events and loads the matching symbols from the operator-configured
///     symbol server into the shared WPP <see cref="DecodingContext" />.
/// </summary>
/// <remarks>
///     Relay data supplies only the PDB name, GUID and age. The server URL and cache are operator configuration, the
///     download goes to the configured host only and never follows redirects. Discovery is best effort: events that
///     arrive while their symbols are still resolving wait in memory for a bounded time; after that, or when the
///     identity only shows up later, they keep the missing-format placeholder. Nothing is replayed.
/// </remarks>
public sealed class GatewaySymbolResolver : IDisposable
{
    /// <summary>Name of the dedicated, no-redirect HTTP client.</summary>
    public const string HttpClientName = "etw-gateway-symbols";

    /// <summary>Identities handled per request; the remainder is picked up by later requests.</summary>
    internal const int MaxIdentitiesPerRequest = 32;

    /// <summary>Total identities remembered, so untrusted senders cannot grow memory without bound.</summary>
    internal const int MaxTrackedIdentities = 4096;

    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(1);

    private readonly SymbolStoreConfiguration? _configuration;
    private readonly DecodingContext? _context;
    private readonly CancellationTokenSource _disposed = new();
    private readonly HttpClient? _httpClient;
    private readonly ConcurrentDictionary<PdbMetaData, Entry> _identities = new();
    private readonly ILogger _logger;
    private readonly RawEtwRelayLimits _limits;
    private int _maxAttempts = 1;
    private int _maxQueued;
    private TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);
    private int _queued;
    private TimeSpan _waitTimeout;
    private readonly TimeProvider _time;

    public GatewaySymbolResolver(
        GatewayOptions.WppOptions options,
        DecodingContext? context,
        HttpClient? httpClient,
        RawEtwRelayLimits limits,
        ILogger logger,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _logger = logger;
        _limits = limits;
        _time = timeProvider ?? TimeProvider.System;

        if (string.IsNullOrWhiteSpace(options.SymbolServer))
        {
            return;
        }

        if (httpClient is null || context is null)
        {
            throw new ArgumentException("A symbol server requires an HTTP client and a decoding context.");
        }

        if (!Uri.TryCreate(options.SymbolServer, UriKind.Absolute, out Uri? server) ||
            (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Gateway:Wpp:SymbolServer must be an absolute http(s) URL.");
        }

        string cache = string.IsNullOrWhiteSpace(options.SymbolCache)
            ? Path.Combine(Path.GetTempPath(), "Nefarius.Utilities.ETW.OtlpGateway", "symbols")
            : options.SymbolCache;
        EnsureWritable(cache);

        _maxAttempts = Math.Max(1, options.SymbolMaxAttempts);
        _maxQueued = Math.Max(0, options.SymbolMaxQueuedRequests);
        _waitTimeout = options.SymbolWaitTimeout;
        _requestTimeout = options.SymbolRequestTimeout > TimeSpan.Zero
            ? options.SymbolRequestTimeout
            : TimeSpan.FromSeconds(30);
        _httpClient = httpClient;
        _configuration = new SymbolStoreConfiguration(cache, server.ToString());
        _logger.LogInformation("Symbol discovery enabled: server {Server}, cache {Cache}.", server, cache);
    }

    /// <summary>Gets whether a symbol server is configured.</summary>
    public bool Enabled => _configuration is not null;

    /// <inheritdoc />
    public void Dispose() => _disposed.Cancel();

    /// <summary>
    ///     Resolves every new PDB identity carried by the request's relay events. Never throws because of symbol
    ///     problems; only the caller's cancellation propagates.
    /// </summary>
    public async Task DiscoverAsync(ExportLogsServiceRequest request, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }
        List<Task> pending = [];
        foreach (PdbMetaData identity in ScanIdentities(request))
        {
            Entry? entry = GetOrStart(identity);
            if (entry is not null)
            {
                pending.Add(entry.Completion);
            }
        }

        // Queue behind every symbol that is still resolving, not just this request's own: another session's
        // download may be exactly what this request's WPP events need.
        foreach (Entry inFlight in _identities.Values)
        {
            if (!inFlight.Completion.IsCompleted)
            {
                pending.Add(inFlight.Completion);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        if (Interlocked.Increment(ref _queued) > _maxQueued)
        {
            Interlocked.Decrement(ref _queued);
            _logger.LogWarning("{Limit} requests already wait for symbols; decoding without waiting.", _maxQueued);
            return;
        }

        try
        {
            // Bounded wait, held in memory. Time-out is not an error: the request is decoded with placeholders and
            // the shared downloads continue for later requests, and even when this caller gives up.
            await Task.WhenAll(pending).WaitAsync(_waitTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Symbols were still resolving after {Timeout}; decoding without them.", _waitTimeout);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
        }
    }

    /// <summary>Extracts the distinct RSDS identities of one request.</summary>
    internal IEnumerable<PdbMetaData> ScanIdentities(ExportLogsServiceRequest request)
    {
        HashSet<PdbMetaData> found = [];
        foreach (var resource in request.ResourceLogs)
        {
            foreach (var scope in resource.ScopeLogs)
            {
                foreach (var record in scope.LogRecords)
                {
                    if (!RelayLogRecords.TryGetFrame(record, out ReadOnlyMemory<byte> bytes))
                    {
                        continue;
                    }

                    if (TryReadRsds(bytes.Span, out PdbMetaData pdb) && found.Add(pdb) &&
                        found.Count >= MaxIdentitiesPerRequest)
                    {
                        return found;
                    }
                }
            }
        }

        return found;
    }

    private bool TryReadRsds(ReadOnlySpan<byte> bytes, out PdbMetaData pdb)
    {
        pdb = default;
        try
        {
            RawEtwFrame frame = RawEtwFrameCodec.Decode(bytes, _limits);
            return frame.Type == RawEtwFrameType.Event && RawRsdsExtractor.TryExtract(frame.Record, out pdb);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Malformed frames are the relay processor's business; discovery just skips them.
            return false;
        }
    }

    private Entry? GetOrStart(PdbMetaData identity)
    {
        if (_identities.TryGetValue(identity, out Entry? existing))
        {
            if (!existing.CanRetry(_time.GetUtcNow()))
            {
                return existing.Completion.IsCompleted ? null : existing;
            }

            // The replacement itself is what gets installed, so its task is the one callers wait on.
            Entry retry = new();
            if (!_identities.TryUpdate(identity, retry, existing))
            {
                return null;
            }

            _ = ResolveAsync(identity, retry);
            return retry;
        }

        if (_identities.Count >= MaxTrackedIdentities && !TryReclaimFailed())
        {
            _logger.LogWarning("Symbol identity tracking limit of {Limit} reached; ignoring new identities.",
                MaxTrackedIdentities);
            return null;
        }

        Entry entry = new();
        Entry winner = _identities.GetOrAdd(identity, entry);
        if (!ReferenceEquals(winner, entry))
        {
            return winner.Completion.IsCompleted ? null : winner;
        }

        _ = ResolveAsync(identity, entry);
        return entry;
    }

    private async Task ResolveAsync(PdbMetaData identity, Entry entry)
    {
        // Let the caller continue on its own thread; resolution runs independently of any request.
        await Task.Yield();

        string name = identity.PdbName;
        try
        {
            ResolvedPdb result = null!;
            for (int attempt = 1; ; attempt++)
            {
                // The deadline covers the request and the body copy. Disposal cancels it too; a deadline on its
                // own is one failed attempt, not the end of resolution.
                using CancellationTokenSource attemptTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
                attemptTimeout.CancelAfter(_requestTimeout);
                try
                {
                    IReadOnlyList<ResolvedPdb> results = await SymbolStore
                        .ResolveAsync([identity], _configuration!, _httpClient, attemptTimeout.Token)
                        .ConfigureAwait(false);
                    result = results[0];
                }
                catch (OperationCanceledException) when (!_disposed.IsCancellationRequested)
                {
                    if (attempt >= _maxAttempts)
                    {
                        _logger.LogWarning("Symbols for {Pdb} timed out after {Attempts} attempt(s).", name, attempt);
                        entry.MarkFailed(_time.GetUtcNow() + FailureRetryDelay);
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), _disposed.Token).ConfigureAwait(false);
                    continue;
                }

                if (result.LocalPath is not null || IsPermanent(result) || attempt >= _maxAttempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), _disposed.Token).ConfigureAwait(false);
            }

            if (result.LocalPath is null)
            {
                _logger.LogWarning("Symbols for {Pdb} ({Guid}, age {Age}) could not be resolved: {Failure}.",
                    name, identity.Guid, identity.Age, result.Failure);
                // A permanent failure (not found, denied, wrong identity) is final; others are retried later.
                entry.MarkFailed(IsPermanent(result)
                    ? DateTimeOffset.MaxValue
                    : _time.GetUtcNow() + FailureRetryDelay);
                return;
            }

            bool added = await Task.Run(() =>
                _context!.TryExtend(new PdbFileDecodingContextType(result.LocalPath)), _disposed.Token)
                .ConfigureAwait(false);

            if (!added)
            {
                _logger.LogWarning("Symbols for {Pdb} conflict with already loaded formats and were rejected.", name);
                entry.MarkFailed(DateTimeOffset.MaxValue);
                return;
            }

            entry.MarkSucceeded();
            _logger.LogInformation("Loaded symbols for {Pdb} ({Origin}).", name, result.Origin);
        }
        catch (OperationCanceledException)
        {
            entry.MarkFailed(_time.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Symbols for {Pdb} could not be loaded: {Error}", name, ex.GetType().Name);
            entry.MarkFailed(_time.GetUtcNow() + FailureRetryDelay);
        }
        finally
        {
            entry.Complete();
        }
    }

    /// <summary>
    ///     Not found, access denied and a mismatching download will not change by trying again; timeouts, network and
    ///     server errors might.
    /// </summary>
    private static bool IsPermanent(ResolvedPdb result) =>
        result.Failure is PdbResolutionFailure.NotFoundOnServer or PdbResolutionFailure.Unauthorized ||
        (result.Failure == PdbResolutionFailure.DownloadFailed &&
         result.Detail?.StartsWith("Downloaded PDB identity", StringComparison.Ordinal) == true);

    /// <summary>
    ///     Drops one finished failure so a new identity can be tracked. Loaded symbols and downloads still running stay.
    /// </summary>
    private bool TryReclaimFailed()
    {
        foreach (KeyValuePair<PdbMetaData, Entry> pair in _identities)
        {
            Entry candidate = pair.Value;
            if (candidate.Completion.IsCompleted && !candidate.Succeeded &&
                _identities.TryRemove(new KeyValuePair<PdbMetaData, Entry>(pair.Key, candidate)))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The symbol cache directory '{directory}' is not writable.", ex);
        }
    }

    private sealed class Entry
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _retryAfterTicks = long.MaxValue;

        public Task Completion => _completion.Task;

        /// <summary>Set once the PDB was loaded. Failures stay reclaimable; this does not.</summary>
        public bool Succeeded => Volatile.Read(ref _succeeded) == 1;

        private int _succeeded;

        public void MarkSucceeded() => Volatile.Write(ref _succeeded, 1);

        public void MarkFailed(DateTimeOffset retryAfter) =>
            Volatile.Write(ref _retryAfterTicks, retryAfter.UtcTicks);

        public void Complete() => _completion.TrySetResult();

        public bool CanRetry(DateTimeOffset now) =>
            Completion.IsCompleted && Volatile.Read(ref _retryAfterTicks) <= now.UtcTicks;
    }
}

/// <summary>
///     Resolves <see cref="GatewaySymbolResolver" /> at host start, so a bad symbol server URL or cache fails fast.
/// </summary>
internal sealed class SymbolResolverStartup(GatewaySymbolResolver resolver) : IHostedService
{
    public GatewaySymbolResolver Resolver { get; } = resolver;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}