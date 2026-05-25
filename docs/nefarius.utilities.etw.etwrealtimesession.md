# EtwRealtimeSession

Namespace: Nefarius.Utilities.ETW

Represents an active real-time ETW session that collects events from user-mode providers
 and delivers them to one or more consumers via [EtwUtil.EnumerateRealtimeEventsAsync(String, Action&lt;EtwJsonConverterOptions&gt;, CancellationToken)](./nefarius.utilities.etw.etwutil.md#enumeraterealtimeeventsasyncstring-actionetwjsonconverteroptions-cancellationtoken).

```csharp
public sealed class EtwRealtimeSession : System.IDisposable
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwRealtimeSession](./nefarius.utilities.etw.etwrealtimesession.md)<br>
Implements [IDisposable](https://docs.microsoft.com/en-us/dotnet/api/system.idisposable)

**Remarks:**

Creating a session requires elevated privileges (administrator) or
 `SeSystemProfilePrivilege`. Sessions persist after the process that created them
 exits; always dispose this object to stop the session, and call
 [EtwUtil.StopOrphanSession(String)](./nefarius.utilities.etw.etwutil.md#stoporphansessionstring) at startup to clean up sessions left behind by
 a previous crash.

This class is thread-safe: [EtwRealtimeSession.EnableProvider(Guid, TraceEventLevel, UInt64, UInt64)](./nefarius.utilities.etw.etwrealtimesession.md#enableproviderguid-traceeventlevel-uint64-uint64), [EtwRealtimeSession.DisableProvider(Guid)](./nefarius.utilities.etw.etwrealtimesession.md#disableproviderguid),
 [EtwRealtimeSession.Flush()](./nefarius.utilities.etw.etwrealtimesession.md#flush), and [EtwRealtimeSession.Dispose()](./nefarius.utilities.etw.etwrealtimesession.md#dispose) may be called from any thread.

## Properties

### <a id="properties-sessionname"/>**SessionName**

Gets the session name passed to [EtwRealtimeSession.Create(String, Action&lt;EtwRealtimeSessionOptions&gt;)](./nefarius.utilities.etw.etwrealtimesession.md#createstring-actionetwrealtimesessionoptions).

```csharp
public string SessionName { get; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

## Methods

### <a id="methods-create"/>**Create(String, Action&lt;EtwRealtimeSessionOptions&gt;)**

Creates and starts a real-time ETW session with the specified name.

```csharp
public static EtwRealtimeSession Create(string sessionName, Action<EtwRealtimeSessionOptions> configure)
```

#### Parameters

`sessionName` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
A unique name for the session (case-insensitive). If a session with this name
 already exists, an [EtwStartTraceException](./nefarius.utilities.etw.exceptions.etwstarttraceexception.md) is thrown with
 ; call
 [EtwUtil.StopOrphanSession(String)](./nefarius.utilities.etw.etwutil.md#stoporphansessionstring) first to remove the orphan.

`configure` [Action&lt;EtwRealtimeSessionOptions&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>
Optional delegate to adjust [EtwRealtimeSessionOptions](./nefarius.utilities.etw.etwrealtimesessionoptions.md).

#### Returns

A running [EtwRealtimeSession](./nefarius.utilities.etw.etwrealtimesession.md) instance. Dispose it to stop the session.

#### Exceptions

[ArgumentNullException](https://docs.microsoft.com/en-us/dotnet/api/system.argumentnullexception)<br>
`sessionName` is .

[ArgumentException](https://docs.microsoft.com/en-us/dotnet/api/system.argumentexception)<br>
`sessionName` is empty or whitespace.

[EtwStartTraceException](./nefarius.utilities.etw.exceptions.etwstarttraceexception.md)<br>
`StartTrace` failed — inspect [Win32Exception.NativeErrorCode](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.win32exception.nativeerrorcode).
 Common causes:  (run as administrator),
  (orphan session still running).

### <a id="methods-disableprovider"/>**DisableProvider(Guid)**

Disables a previously enabled provider on this session.

```csharp
public void DisableProvider(Guid providerGuid)
```

#### Parameters

`providerGuid` [Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>
The provider's registration GUID.

#### Exceptions

[ObjectDisposedException](https://docs.microsoft.com/en-us/dotnet/api/system.objectdisposedexception)<br>
The session has been disposed.

[EtwEnableTraceException](./nefarius.utilities.etw.exceptions.etwenabletraceexception.md)<br>
`EnableTraceEx2` returned a non-zero error code.

### <a id="methods-dispose"/>**Dispose()**

```csharp
public void Dispose()
```

**Remarks:**

Stops the ETW session (via `ControlTraceW` with `EVENT_TRACE_CONTROL_STOP`).
 It is safe to call [EtwRealtimeSession.Dispose()](./nefarius.utilities.etw.etwrealtimesession.md#dispose) more than once.
 If the native stop call fails the session may persist as an orphan;
 use [EtwUtil.StopOrphanSession(String)](./nefarius.utilities.etw.etwutil.md#stoporphansessionstring) at next startup to recover.

### <a id="methods-enableprovider"/>**EnableProvider(Guid, TraceEventLevel, UInt64, UInt64)**

Enables a provider on this session, causing it to send events to all real-time consumers.

```csharp
public void EnableProvider(Guid providerGuid, TraceEventLevel level, ulong matchAnyKeyword, ulong matchAllKeyword)
```

#### Parameters

`providerGuid` [Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>
The provider's registration GUID.

`level` [TraceEventLevel](./nefarius.utilities.etw.traceeventlevel.md)<br>
Maximum event severity level to receive. Defaults to [TraceEventLevel.Verbose](./nefarius.utilities.etw.traceeventlevel.md#verbose).

`matchAnyKeyword` [UInt64](https://docs.microsoft.com/en-us/dotnet/api/system.uint64)<br>
Bitmask: an event is included if any of the provider's keywords match.
 Pass [UInt64.MaxValue](https://docs.microsoft.com/en-us/dotnet/api/system.uint64.maxvalue) (default) to receive all events.

`matchAllKeyword` [UInt64](https://docs.microsoft.com/en-us/dotnet/api/system.uint64)<br>
Bitmask: an event is included only if all of these keywords are set.
 Pass `0` (default) to disable the all-keyword filter.

#### Exceptions

[ObjectDisposedException](https://docs.microsoft.com/en-us/dotnet/api/system.objectdisposedexception)<br>
The session has been disposed.

[EtwEnableTraceException](./nefarius.utilities.etw.exceptions.etwenabletraceexception.md)<br>
`EnableTraceEx2` returned a non-zero error code.

### <a id="methods-flush"/>**Flush()**

Flushes any in-flight event buffers to real-time consumers immediately,
 without waiting for the next flush-timer tick.

```csharp
public void Flush()
```

#### Exceptions

[ObjectDisposedException](https://docs.microsoft.com/en-us/dotnet/api/system.objectdisposedexception)<br>
The session has been disposed.

[Win32Exception](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.win32exception)<br>
`ControlTrace(FLUSH)` returned a non-zero error code.
