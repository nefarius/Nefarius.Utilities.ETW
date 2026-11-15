# EtwRealtimeSessionOptions

Namespace: Nefarius.Utilities.ETW

Buffer and timing options for [EtwRealtimeSession](./nefarius.utilities.etw.etwrealtimesession.md).

```csharp
public sealed class EtwRealtimeSessionOptions
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwRealtimeSessionOptions](./nefarius.utilities.etw.etwrealtimesessionoptions.md)

## Properties

### <a id="properties-buffersizekb"/>**BufferSizeKb**

The size of each ETW buffer in kilobytes. Defaults to `64` KB.

```csharp
public uint BufferSizeKb { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-clockresolution"/>**ClockResolution**

Clock resolution used to timestamp events.
 Defaults to [EtwClockResolution.QueryPerformanceCounter](./nefarius.utilities.etw.etwclockresolution.md#queryperformancecounter).

```csharp
public EtwClockResolution ClockResolution { get; set; }
```

#### Property Value

[EtwClockResolution](./nefarius.utilities.etw.etwclockresolution.md)<br>

### <a id="properties-enableflags"/>**EnableFlags**

Classic kernel event groups (`EVENT_TRACE_FLAG_*`) for a session named
 [EtwRealtimeSession.NtKernelLoggerName](./nefarius.utilities.etw.etwrealtimesession.md#ntkernelloggername), which is created as the NT Kernel Logger. Ignored for
 other sessions. Defaults to `0`.

```csharp
public uint EnableFlags { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-flushtimerseconds"/>**FlushTimerSeconds**

How often, in seconds, the session flushes non-full buffers to consumers.
 Defaults to `1` second. Lower values reduce event delivery latency at
 the cost of increased flush overhead.

```csharp
public uint FlushTimerSeconds { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-maximumbuffers"/>**MaximumBuffers**

Maximum number of ETW buffers the session may allocate.
 When set to `0` (the default), the value is computed as twice
 [EtwRealtimeSessionOptions.MinimumBuffers](./nefarius.utilities.etw.etwrealtimesessionoptions.md#minimumbuffers) at session-creation time.

```csharp
public uint MaximumBuffers { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-minimumbuffers"/>**MinimumBuffers**

Minimum number of ETW buffers to allocate.
 Defaults to [Environment.ProcessorCount](https://learn.microsoft.com/dotnet/api/system.environment.processorcount).

```csharp
public uint MinimumBuffers { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-reporterror"/>**ReportError**

Reports non-fatal errors encountered during session management.

```csharp
public Action<String> ReportError { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-systemlogger"/>**SystemLogger**

Creates a system logger session (`EVENT_TRACE_SYSTEM_LOGGER_MODE`, available from Windows 8) that can
 receive the kernel providers listed in [EtwSystemProviders](./nefarius.utilities.etw.etwsystemproviders.md) through
 [EtwRealtimeSession.EnableProvider(Guid, TraceEventLevel, UInt64, UInt64)](./nefarius.utilities.etw.etwrealtimesession.md#enableproviderguid-traceeventlevel-uint64-uint64). Enabling those system provider GUIDs requires Windows 11
 or later; on older versions the session starts but no events are documented to arrive. Any session name is accepted, so it does not collide
 with the single, system-wide [EtwRealtimeSession.NtKernelLoggerName](./nefarius.utilities.etw.etwrealtimesession.md#ntkernelloggername) session.
 Defaults to `false`.

```csharp
public bool SystemLogger { get; set; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>
