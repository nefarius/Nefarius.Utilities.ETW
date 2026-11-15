using System.Collections.Concurrent;

using Google.Protobuf;

using Nefarius.Utilities.ETW.Events;
using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Settings of a <see cref="RelayLogProcessor" />.
/// </summary>
public sealed class RelayLogProcessorOptions
{
    /// <summary>
    ///     Configures decoding for every new session, notably
    ///     <see cref="EtwJsonConverterOptions.WppDecodingContext" /> with the server-side PDB/TMF data. Called once per
    ///     session.
    /// </summary>
    public Action<EtwJsonConverterOptions>? DecodingOptions { get; set; }

    /// <summary>Frame limits applied to every session.</summary>
    public RawEtwRelayLimits Limits { get; set; } = RawEtwRelayLimits.Default;

    /// <summary>Maximum concurrently tracked sessions over all tenants. Default: 1024.</summary>
    public int MaxSessions { get; set; } = 1024;

    /// <summary>Maximum concurrently tracked sessions of one tenant. Default: 32.</summary>
    public int MaxSessionsPerTenant { get; set; } = 32;

    /// <summary>A session without frames for this long is discarded. Default: 15 minutes.</summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Maximum number of log records handled per request. Default: 10000.</summary>
    public int MaxRecordsPerRequest { get; set; } = 10_000;

    /// <summary>Source of time for idle expiry. Defaults to <see cref="TimeProvider.System" />.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>
///     The outcome of processing one export request.
/// </summary>
public sealed class RelayLogResult
{
    /// <summary>Decoded log records, grouped by the resource of the records they came from.</summary>
    public ExportLogsServiceRequest Decoded { get; } = new();

    /// <summary>The number of input records that were applied or recognized as retries.</summary>
    public long Accepted { get; internal set; }

    /// <summary>The number of input records that were rejected.</summary>
    public long Rejected { get; internal set; }

    /// <summary>A description of the first rejection, or <see langword="null" />.</summary>
    public string? Error { get; internal set; }

    /// <summary>The number of events the producers reported as dropped in this request.</summary>
    public ulong LostEvents { get; internal set; }

    /// <summary>The OTLP response to return to the sender.</summary>
    public ExportLogsServiceResponse ToResponse()
    {
        ExportLogsServiceResponse response = new();
        if (Rejected > 0)
        {
            response.PartialSuccess = new ExportLogsPartialSuccess
            {
                RejectedLogRecords = Rejected,
                ErrorMessage = Error ?? "Records were rejected."
            };
        }

        return response;
    }
}

/// <summary>
///     Decodes raw ETW relay frames carried in OTLP Logs and produces structured OTLP log records. This is the
///     engine behind a decoding gateway; it is independent of the HTTP or gRPC front end and of authentication.
/// </summary>
/// <remarks>
///     <para>
///         Sessions are keyed by tenant and by the relay session identifier in the resource attributes. The tenant is
///         supplied by the caller from the authenticated identity, so one tenant can neither read nor disturb the
///         sessions of another, whatever session identifier it announces.
///     </para>
///     <para>
///         A session starts with a hello frame. A batch that continues a session the processor does not know (it
///         expired or the gateway restarted) is rejected so the producer starts a new session; parser state cannot be
///         reconstructed from the middle of a stream.
///     </para>
///     <para>
///         Within a session, requests must be processed in order. Concurrent requests for the same session are
///         serialized, but their order is then undefined.
///     </para>
/// </remarks>
public sealed class RelayLogProcessor : IDisposable
{
    private readonly RelayLogProcessorOptions _options;
    private readonly ConcurrentDictionary<SessionKey, Session> _sessions = new();
    private readonly object _admission = new();
    private readonly object _sweepGate = new();
    private long _lastSweepTicks;

    /// <summary>
    ///     Creates the processor.
    /// </summary>
    public RelayLogProcessor(RelayLogProcessorOptions? options = null)
    {
        _options = options ?? new RelayLogProcessorOptions();
        _lastSweepTicks = _options.TimeProvider.GetTimestamp();
    }

    /// <summary>The number of sessions currently tracked.</summary>
    public int ActiveSessions => _sessions.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (KeyValuePair<SessionKey, Session> entry in _sessions)
        {
            if (_sessions.TryRemove(entry.Key, out Session? session))
            {
                session.Decoder.Dispose();
            }
        }
    }

    /// <summary>
    ///     Decodes the relay frames of an export request.
    /// </summary>
    /// <param name="tenant">An identifier of the authenticated sender; sessions are isolated per tenant.</param>
    /// <param name="request">The request carrying relay frames as <c>bytes_value</c> bodies.</param>
    /// <returns>Decoded records and per-record accounting; protocol violations reject only the affected records.</returns>
    public RelayLogResult Process(string tenant, ExportLogsServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(request);

        SweepIdleSessions();

        RelayLogResult result = new();
        long processed = 0;

        foreach (ResourceLogs resourceLogs in request.ResourceLogs)
        {
            ResourceLogs? output = null;
            bool hasSession = RelayLogRecords.TryGetSessionId(resourceLogs.Resource, out Guid sessionId);

            foreach (ScopeLogs scopeLogs in resourceLogs.ScopeLogs)
            {
                ScopeLogs? outputScope = null;

                foreach (LogRecord record in scopeLogs.LogRecords)
                {
                    if (++processed > _options.MaxRecordsPerRequest)
                    {
                        Reject(result, "The request exceeds the maximum number of log records.");
                        continue;
                    }

                    if (!hasSession)
                    {
                        Reject(result, $"The resource has no valid '{EtwOtlpAttributes.RelaySessionId}' attribute.");
                        continue;
                    }

                    if (!RelayLogRecords.TryGetFrame(record, out ReadOnlyMemory<byte> frame))
                    {
                        Reject(result, "The log record does not carry a relay frame in its bytes body.");
                        continue;
                    }

                    if (!TryProcessFrame(tenant, sessionId, frame, result, out List<LogRecord>? decoded))
                    {
                        continue;
                    }

                    if (decoded is { Count: > 0 })
                    {
                        output ??= CreateOutput(resourceLogs, result);
                        outputScope ??= CreateScope(output);
                        outputScope.LogRecords.AddRange(decoded);
                    }
                }
            }
        }

        return result;
    }

    private bool TryProcessFrame(string tenant, Guid sessionId, ReadOnlyMemory<byte> frame, RelayLogResult result,
        out List<LogRecord>? decoded)
    {
        decoded = null;
        SessionKey key = new(tenant, sessionId);

        if (!_sessions.TryGetValue(key, out Session? session))
        {
            // Only a hello frame can open a session.
            if (!RelayLogRecords.TryReadPrefix(frame.Span, out RawEtwFrameType type, out _) ||
                type != RawEtwFrameType.Hello)
            {
                Reject(result,
                    "The session is unknown (expired or never started); start a new session with a hello frame.");
                return false;
            }

            session = TryCreateSession(key, result);
            if (session is null)
            {
                return false;
            }
        }

        List<LogRecord> events = [];
        try
        {
            lock (session)
            {
                // The sweep removes a session under this same lock, so a refresh of LastActivity cannot lose the race
                // to disposal, and a session removed before we entered is not decoded.
                if (!_sessions.TryGetValue(key, out Session? current) || !ReferenceEquals(current, session))
                {
                    Reject(result, "The session is unknown (expired or never started); start a new session with a hello frame.");
                    return false;
                }

                session.LastActivity = _options.TimeProvider.GetTimestamp();
                session.Decoder.Process(frame.Span, e => events.Add(EtwLogRecordMapper.ToLogRecord(e, session.CurrentSequence)));
                ulong lost = session.Decoder.LostEvents;
                result.LostEvents += lost - session.ReportedLoss;
                session.ReportedLoss = lost;
            }

            result.Accepted++;
            // An ended session stays until it expires, so an exact retry of a frame still in the decoder's retry
            // window is recognized instead of looking like an unknown session. Idle sweeping bounds the retention.
        }
        catch (RawEtwProtocolException ex)
        {
            // A rejected frame leaves an established session as it was: the sender can retry or continue. A session
            // whose hello was rejected never became usable and must not keep occupying a slot.
            if (session.Decoder.SessionId is null)
            {
                RemoveSession(key);
            }

            Reject(result, ex.Message);
            return false;
        }

        decoded = events;
        return true;
    }

    private Session? TryCreateSession(SessionKey key, RelayLogResult result)
    {
        // Admission and insertion are one critical section, shared with removal, so concurrent hellos cannot pass
        // the limit check against the same free slot.
        lock (_admission)
        {
            int perTenant = 0;
            foreach (SessionKey existing in _sessions.Keys)
            {
                if (existing.Tenant == key.Tenant)
                {
                    perTenant++;
                }
            }

            if (_sessions.Count >= _options.MaxSessions || perTenant >= _options.MaxSessionsPerTenant)
            {
                Reject(result, "Too many active sessions.");
                return null;
            }

            Session created = new(new RawEtwRelayDecoder(_options.DecodingOptions, new RawEtwRelayOptions { Limits = _options.Limits }),
                _options.TimeProvider.GetTimestamp());

            if (!_sessions.TryAdd(key, created))
            {
                created.Decoder.Dispose();
                return _sessions[key];
            }

            return created;
        }
    }

    private void RemoveSession(SessionKey key)
    {
        lock (_admission)
        {
            if (_sessions.TryRemove(key, out Session? session))
            {
                lock (session)
                {
                    session.Decoder.Dispose();
                }
            }
        }
    }

    private void SweepIdleSessions()
    {
        TimeProvider time = _options.TimeProvider;
        long now = time.GetTimestamp();
        TimeSpan interval = TimeSpan.FromTicks(Math.Max(_options.SessionIdleTimeout.Ticks / 4, TimeSpan.TicksPerSecond));

        lock (_sweepGate)
        {
            if (time.GetElapsedTime(_lastSweepTicks, now) < interval)
            {
                return;
            }

            _lastSweepTicks = now;
        }

        foreach (KeyValuePair<SessionKey, Session> entry in _sessions)
        {
            RemoveIfStillExpired(entry.Key, entry.Value, time, now);
        }
    }

    /// <summary>
    ///     Removes <paramref name="session" /> only when it is still the current value and still idle. The activity
    ///     timestamp is read under the session lock, which is the lock that refreshes it.
    /// </summary>
    private void RemoveIfStillExpired(SessionKey key, Session session, TimeProvider time, long now)
    {
        lock (_admission)
        {
            if (!_sessions.TryGetValue(key, out Session? current) || !ReferenceEquals(current, session))
            {
                return;
            }

            lock (session)
            {
                if (time.GetElapsedTime(session.LastActivity, now) <= _options.SessionIdleTimeout)
                {
                    return;
                }

                if (_sessions.TryRemove(key, out _))
                {
                    session.Decoder.Dispose();
                }
            }
        }
    }

    private static ResourceLogs CreateOutput(ResourceLogs input, RelayLogResult result)
    {
        // The decoded records keep the sender's resource attributes (host, service, ...).
        ResourceLogs output = new() { Resource = input.Resource?.Clone(), SchemaUrl = input.SchemaUrl };
        result.Decoded.ResourceLogs.Add(output);
        return output;
    }

    private static ScopeLogs CreateScope(ResourceLogs output)
    {
        ScopeLogs scope = new()
        {
            Scope = new InstrumentationScope { Name = "Nefarius.Utilities.ETW" }
        };
        output.ScopeLogs.Add(scope);
        return scope;
    }

    private static void Reject(RelayLogResult result, string message)
    {
        result.Rejected++;
        result.Error ??= message;
    }

    private readonly record struct SessionKey(string Tenant, Guid SessionId);

    private sealed class Session(RawEtwRelayDecoder decoder, long created)
    {
        public RawEtwRelayDecoder Decoder { get; } = decoder;

        public long LastActivity { get; set; } = created;

        public ulong ReportedLoss { get; set; }

        public ulong CurrentSequence => Decoder.NextSequence - 1;
    }
}
