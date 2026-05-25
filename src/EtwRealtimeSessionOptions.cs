namespace Nefarius.Utilities.ETW;

/// <summary>
///     Buffer and timing options for <see cref="EtwRealtimeSession" />.
/// </summary>
public sealed class EtwRealtimeSessionOptions
{
    internal EtwRealtimeSessionOptions() { }

    /// <summary>
    ///     Reports non-fatal errors encountered during session management.
    /// </summary>
    public Action<string>? ReportError { get; set; }

    /// <summary>
    ///     The size of each ETW buffer in kilobytes. Defaults to <c>64</c> KB.
    /// </summary>
    public uint BufferSizeKb { get; set; } = 64;

    /// <summary>
    ///     Minimum number of ETW buffers to allocate.
    ///     Defaults to <see cref="Environment.ProcessorCount" />.
    /// </summary>
    public uint MinimumBuffers { get; set; } = (uint)Environment.ProcessorCount;

    /// <summary>
    ///     Maximum number of ETW buffers the session may allocate.
    ///     When set to <c>0</c> (the default), the value is computed as twice
    ///     <see cref="MinimumBuffers" /> at session-creation time.
    /// </summary>
    public uint MaximumBuffers { get; set; }

    /// <summary>
    ///     How often, in seconds, the session flushes non-full buffers to consumers.
    ///     Defaults to <c>1</c> second. Lower values reduce event delivery latency at
    ///     the cost of increased flush overhead.
    /// </summary>
    public uint FlushTimerSeconds { get; set; } = 1;

    /// <summary>
    ///     Clock resolution used to timestamp events.
    ///     Defaults to <see cref="EtwClockResolution.QueryPerformanceCounter" />.
    /// </summary>
    public EtwClockResolution ClockResolution { get; set; } = EtwClockResolution.QueryPerformanceCounter;
}

/// <summary>
///     Clock source used to timestamp ETW events.
///     Corresponds to the <c>ClientContext</c> field of <c>WNODE_HEADER</c>.
/// </summary>
public enum EtwClockResolution : uint
{
    /// <summary>
    ///     Query Performance Counter — highest precision, correlated to wall clock.
    ///     This is the recommended default.
    /// </summary>
    QueryPerformanceCounter = 1,

    /// <summary>
    ///     System time in 100-nanosecond intervals since 1 January 1601.
    ///     Lower overhead but coarser resolution than <see cref="QueryPerformanceCounter" />.
    /// </summary>
    SystemTime = 2,

    /// <summary>
    ///     CPU cycle counter. Highest frequency but not correlated to wall-clock time and
    ///     may not be comparable across processors.
    /// </summary>
    CpuCycle = 3
}
