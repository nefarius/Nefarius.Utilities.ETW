using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     Represents an active real-time ETW session that collects events from user-mode providers
///     and delivers them to one or more consumers via <see cref="EtwUtil.EnumerateRealtimeEventsAsync" />.
/// </summary>
/// <remarks>
///     <para>
///         Creating a session requires elevated privileges (administrator) or
///         <c>SeSystemProfilePrivilege</c>. Sessions persist after the process that created them
///         exits; always dispose this object to stop the session, and call
///         <see cref="EtwUtil.StopOrphanSession" /> at startup to clean up sessions left behind by
///         a previous crash.
///     </para>
///     <para>
///         This class is thread-safe: <see cref="EnableProvider" />, <see cref="DisableProvider" />,
///         <see cref="Flush" />, and <see cref="Dispose" /> may be called from any thread.
///     </para>
/// </remarks>
public sealed class EtwRealtimeSession : IDisposable
{
    private readonly string _sessionName;
    private readonly object _lock = new();
    private CONTROLTRACE_HANDLE _sessionHandle;
    private bool _disposed;

    private EtwRealtimeSession(string sessionName, CONTROLTRACE_HANDLE sessionHandle)
    {
        _sessionName = sessionName;
        _sessionHandle = sessionHandle;
    }

    /// <summary>
    ///     Gets the session name passed to <see cref="Create" />.
    /// </summary>
    public string SessionName => _sessionName;

    /// <summary>
    ///     Creates and starts a real-time ETW session with the specified name.
    /// </summary>
    /// <param name="sessionName">
    ///     A unique name for the session (case-insensitive). If a session with this name
    ///     already exists, an <see cref="EtwStartTraceException" /> is thrown with
    ///     <see cref="WIN32_ERROR.ERROR_ALREADY_EXISTS" />; call
    ///     <see cref="EtwUtil.StopOrphanSession" /> first to remove the orphan.
    /// </param>
    /// <param name="configure">Optional delegate to adjust <see cref="EtwRealtimeSessionOptions" />.</param>
    /// <returns>A running <see cref="EtwRealtimeSession" /> instance. Dispose it to stop the session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sessionName" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionName" /> is empty or whitespace.</exception>
    /// <exception cref="EtwStartTraceException">
    ///     <c>StartTrace</c> failed — inspect <see cref="System.ComponentModel.Win32Exception.NativeErrorCode" />.
    ///     Common causes: <see cref="WIN32_ERROR.ERROR_ACCESS_DENIED" /> (run as administrator),
    ///     <see cref="WIN32_ERROR.ERROR_ALREADY_EXISTS" /> (orphan session still running).
    /// </exception>
    public static EtwRealtimeSession Create(string sessionName, Action<EtwRealtimeSessionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Session name must not be empty or whitespace.", nameof(sessionName));
        }

        EtwRealtimeSessionOptions opts = new();
        configure?.Invoke(opts);

        unsafe
        {
            int propsSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
            int nameBytes = (sessionName.Length + 1) * sizeof(char);
            int totalSize = propsSize + nameBytes;

            EVENT_TRACE_PROPERTIES* props = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)totalSize);
            try
            {
                uint maxBufs = opts.MaximumBuffers == 0 ? opts.MinimumBuffers * 2 : opts.MaximumBuffers;

                props->Wnode.BufferSize = (uint)totalSize;
                props->Wnode.Flags = Etw.WNODE_FLAG_TRACED_GUID;
                props->Wnode.ClientContext = (uint)opts.ClockResolution;
                props->BufferSize = opts.BufferSizeKb;
                props->MinimumBuffers = opts.MinimumBuffers;
                props->MaximumBuffers = maxBufs;
                props->FlushTimer = opts.FlushTimerSeconds;
                props->LogFileMode = Etw.EVENT_TRACE_REAL_TIME_MODE | Etw.EVENT_TRACE_NO_PER_PROCESSOR_BUFFERING;
                props->LoggerNameOffset = (uint)propsSize;

                CopySessionName(sessionName, (byte*)props, propsSize);

                // Unsafe.AsRef converts the native pointer to a managed 'ref' so CsWin32's
                // StartTrace(out CONTROLTRACE_HANDLE, string, ref EVENT_TRACE_PROPERTIES) can
                // access both the struct fields and the name bytes appended after the struct.
                ref EVENT_TRACE_PROPERTIES propsRef = ref Unsafe.AsRef<EVENT_TRACE_PROPERTIES>(props);
                CONTROLTRACE_HANDLE handle;
                uint error = PInvoke.StartTrace(out handle, sessionName, ref propsRef);
                if (error != 0)
                {
                    throw new EtwStartTraceException(error, sessionName);
                }

                return new EtwRealtimeSession(sessionName, handle);
            }
            finally
            {
                NativeMemory.Free(props);
            }
        }
    }

    /// <summary>
    ///     Enables a provider on this session, causing it to send events to all real-time consumers.
    /// </summary>
    /// <param name="providerGuid">The provider's registration GUID.</param>
    /// <param name="level">Maximum event severity level to receive. Defaults to <see cref="TraceEventLevel.Verbose" />.</param>
    /// <param name="matchAnyKeyword">
    ///     Bitmask: an event is included if <em>any</em> of the provider's keywords match.
    ///     Pass <see cref="ulong.MaxValue" /> (default) to receive all events.
    /// </param>
    /// <param name="matchAllKeyword">
    ///     Bitmask: an event is included only if <em>all</em> of these keywords are set.
    ///     Pass <c>0</c> (default) to disable the all-keyword filter.
    /// </param>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="EtwEnableTraceException"><c>EnableTraceEx2</c> returned a non-zero error code.</exception>
    public void EnableProvider(
        Guid providerGuid,
        TraceEventLevel level = TraceEventLevel.Verbose,
        ulong matchAnyKeyword = ulong.MaxValue,
        ulong matchAllKeyword = 0)
    {
        ChangeProvider(providerGuid, level, matchAnyKeyword, matchAllKeyword,
            Etw.EVENT_CONTROL_CODE_ENABLE_PROVIDER);
    }

    /// <summary>
    ///     Disables a previously enabled provider on this session.
    /// </summary>
    /// <param name="providerGuid">The provider's registration GUID.</param>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="EtwEnableTraceException"><c>EnableTraceEx2</c> returned a non-zero error code.</exception>
    public void DisableProvider(Guid providerGuid)
    {
        ChangeProvider(providerGuid, TraceEventLevel.None, 0, 0,
            Etw.EVENT_CONTROL_CODE_DISABLE_PROVIDER);
    }

    /// <summary>
    ///     Flushes any in-flight event buffers to real-time consumers immediately,
    ///     without waiting for the next flush-timer tick.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    public void Flush()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ControlSession(Etw.EVENT_TRACE_CONTROL_FLUSH);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Stops the ETW session (via <c>ControlTraceW</c> with <c>EVENT_TRACE_CONTROL_STOP</c>).
    ///     It is safe to call <see cref="Dispose" /> more than once.
    /// </remarks>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ControlSession(Etw.EVENT_TRACE_CONTROL_STOP);
            _sessionHandle = default;
        }
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    private void ChangeProvider(Guid providerGuid, TraceEventLevel level,
        ulong matchAnyKeyword, ulong matchAllKeyword, uint controlCode)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            unsafe
            {
                // EnableTraceEx2 takes 'in Guid' (not Guid*); C# passes it by reference automatically.
                uint error = PInvoke.EnableTraceEx2(
                    _sessionHandle,
                    in providerGuid,
                    controlCode,
                    (byte)level,
                    matchAnyKeyword,
                    matchAllKeyword,
                    0,       // Timeout=0: return immediately (async enable)
                    null);   // no filter

                if (error != 0)
                {
                    throw new EtwEnableTraceException(error, providerGuid, _sessionName);
                }
            }
        }
    }

    private void ControlSession(uint controlCode)
    {
        // ControlSession must be called with _lock held and _sessionHandle != 0.
        unsafe
        {
            int propsSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
            int nameBytes = (_sessionName.Length + 1) * sizeof(char);
            int totalSize = propsSize + nameBytes;

            EVENT_TRACE_PROPERTIES* props = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)totalSize);
            try
            {
                props->Wnode.BufferSize = (uint)totalSize;
                props->LoggerNameOffset = (uint)propsSize;

                CopySessionName(_sessionName, (byte*)props, propsSize);

                // Convert native pointer → managed ref for CsWin32's
                // ControlTrace(CONTROLTRACE_HANDLE, string, ref EVENT_TRACE_PROPERTIES, EVENT_TRACE_CONTROL).
                // Passing the handle means Windows identifies the session without searching by name.
                ref EVENT_TRACE_PROPERTIES propsRef = ref Unsafe.AsRef<EVENT_TRACE_PROPERTIES>(props);
                PInvoke.ControlTrace(
                    _sessionHandle,
                    _sessionName,
                    ref propsRef,
                    (EVENT_TRACE_CONTROL)controlCode);
            }
            finally
            {
                NativeMemory.Free(props);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void CopySessionName(string name, byte* buffer, int propsSize)
    {
        char* dest = (char*)(buffer + propsSize);
        for (int i = 0; i < name.Length; i++)
        {
            dest[i] = name[i];
        }

        dest[name.Length] = '\0';
    }
}
