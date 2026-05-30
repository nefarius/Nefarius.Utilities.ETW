# EtwUtil

Namespace: Nefarius.Utilities.ETW

ETW utility class.

```csharp
public static class EtwUtil
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwUtil](./nefarius.utilities.etw.etwutil.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Methods

### <a id="methods-convertrealtimetojson"/>**ConvertRealtimeToJson(Utf8JsonWriter, String, Action&lt;EtwJsonConverterOptions&gt;, CancellationToken)**

Converts a live real-time ETW session to a stream of JSON objects written to
 `jsonWriter`, blocking until the session ends or
 `cancellationToken` is cancelled.

```csharp
public static bool ConvertRealtimeToJson(Utf8JsonWriter jsonWriter, string sessionName, Action<EtwJsonConverterOptions> options, CancellationToken cancellationToken)
```

#### Parameters

`jsonWriter` [Utf8JsonWriter](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonwriter)<br>
The target JSON writer to write to.

`sessionName` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
The name of an already-running real-time ETW session (e.g., created via
 [EtwRealtimeSession.Create(String, Action&lt;EtwRealtimeSessionOptions&gt;)](./nefarius.utilities.etw.etwrealtimesession.md#createstring-actionetwrealtimesessionoptions) or `logman start`).

`options` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)><br>
Options to further tweak the parsing operation.

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>
Token that, when cancelled, stops trace processing and returns from this method.

#### Returns

`true` when the session ended normally or was cancelled;
 `false` if the session could not be opened.

#### Exceptions

[ArgumentNullException](https://learn.microsoft.com/dotnet/api/system.argumentnullexception)<br>
`jsonWriter` or `sessionName` is `null`.

[ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception)<br>
`sessionName` is empty or whitespace.

**Remarks:**

WPP decoding in real-time mode requires a pre-built
 [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext) supplied via
 `options` — the file-based
 [EtwUtil.EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)](./nefarius.utilities.etw.etwutil.md#enumeratepdbreferencesienumerablestring-actionetwmetadatascanoptions) pre-scan cannot be applied to live sessions.

### <a id="methods-converttojson"/>**ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;)**

Converts one or more .ETL files to a JSON object.

```csharp
public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<String> inputFiles, Action<EtwJsonConverterOptions> options)
```

#### Parameters

`jsonWriter` [Utf8JsonWriter](https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonwriter)<br>
The target JSON writer to write to.

`inputFiles` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
One or more input files.

`options` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)><br>
Options to further tweak the parsing operation.

#### Returns

True on success, false otherwise.

### <a id="methods-enumerateeventsasync"/>**EnumerateEventsAsync(IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;, CancellationToken)**

Converts one or more .ETL files to a stream of UTF-8 JSON objects, yielding each decoded event
 as a [ReadOnlyMemory<byte>](https://learn.microsoft.com/dotnet/api/system.readonlymemory-1) as it is produced.

```csharp
public static IAsyncEnumerable<ReadOnlyMemory<Byte>> EnumerateEventsAsync(IEnumerable<String> inputFiles, Action<EtwJsonConverterOptions> options, CancellationToken cancellationToken)
```

#### Parameters

`inputFiles` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
One or more input files.

`options` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)><br>
Options to further tweak the parsing operation.

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>
Token that, when cancelled, stops trace processing and completes the enumeration.

#### Returns

An [IAsyncEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.iasyncenumerable-1)<T> of raw UTF-8 JSON buffers, one per event. Each buffer contains a
 self-contained JSON object — `{"Event":{"Timestamp":…,"Properties":[{…}]}}` — with no outer
 array wrapper. The caller may concatenate or wrap the items as needed.

#### Exceptions

[ArgumentNullException](https://learn.microsoft.com/dotnet/api/system.argumentnullexception)<br>
`inputFiles` is `null`.

[ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception)<br>
One or more entries in `inputFiles` are `null`, empty, or consist only of
 whitespace.

[EtwOpenTraceException](./nefarius.utilities.etw.exceptions.etwopentraceexception.md)<br>
One of the input files could not be opened by the ETW API.

**Remarks:**

Each [ReadOnlyMemory<byte>](https://learn.microsoft.com/dotnet/api/system.readonlymemory-1) is backed by a buffer rented from
 [ArrayPool&lt;T&gt;.Shared](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.shared). The buffer is valid only until the next iteration step; the library
 returns it to the pool when `MoveNextAsync` is called (i.e. at the start of the next
 `await foreach` loop body). Do not retain references to the memory across iterations.

Trace processing runs on a dedicated background thread so the thread-pool is not blocked by the
 long-running native `ProcessTrace` call. A bounded channel with a fixed capacity couples the
 producer to the consumer, applying natural backpressure when the consumer is slower than the trace.

### <a id="methods-enumeratepdbreferences"/>**EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)**

Performs a lightweight pre-scan of one or more `.ETL` files and returns the deduplicated set of
 [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) entries discovered in the trace.

```csharp
public static IReadOnlyCollection<PdbMetaData> EnumeratePdbReferences(IEnumerable<String> inputFiles, Action<EtwMetadataScanOptions> options)
```

#### Parameters

`inputFiles` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
One or more input `.etl` files to scan. Must not be `null` and must not
 contain any `null` or whitespace-only entries.

`options` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwMetadataScanOptions](./nefarius.utilities.etw.etwmetadatascanoptions.md)><br>
Optional callback to configure scan behaviour. Use it to subscribe to
 [EtwMetadataScanOptions.OnDbgIdRsds](./nefarius.utilities.etw.etwmetadatascanoptions.md#ondbgidrsds) (`KernelTraceControl/ImageID/DbgID_RSDS`),
 [EtwMetadataScanOptions.OnKernelDbgIdRsds](./nefarius.utilities.etw.etwmetadatascanoptions.md#onkerneldbgidrsds) (`MSNT_SystemTrace/EventTrace/DbgIdRSDS`),
 [EtwMetadataScanOptions.OnImageId](./nefarius.utilities.etw.etwmetadatascanoptions.md#onimageid) (`KernelTraceControl/ImageID`), and
 [EtwMetadataScanOptions.OnImageIdFileVersion](./nefarius.utilities.etw.etwmetadatascanoptions.md#onimageidfileversion) (`KernelTraceControl/ImageID/FileVersion`)
 event notifications, or to provide a [EtwMetadataScanOptions.ReportError](./nefarius.utilities.etw.etwmetadatascanoptions.md#reporterror) handler.

#### Returns

A deduplicated, read-only collection of every [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) referenced by the trace.
 The caller should use these entries to locate or download the corresponding `.pdb` files (or find
 matching `.tmf` files), then pass the resulting [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) to
 [EtwUtil.ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;)](./nefarius.utilities.etw.etwutil.md#converttojsonutf8jsonwriter-ienumerablestring-actionetwjsonconverteroptions) via [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext).

#### Exceptions

[ArgumentNullException](https://learn.microsoft.com/dotnet/api/system.argumentnullexception)<br>
`inputFiles` is `null`.

[ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception)<br>
One or more entries in `inputFiles` are `null`, empty, or consist only of
 whitespace.

### <a id="methods-enumeraterealtimeeventsasync"/>**EnumerateRealtimeEventsAsync(String, Action&lt;EtwJsonConverterOptions&gt;, CancellationToken)**

Streams decoded events from a live real-time ETW session as UTF-8 JSON objects,
 yielding each event as a [ReadOnlyMemory<byte>](https://learn.microsoft.com/dotnet/api/system.readonlymemory-1)
 as it is produced.

```csharp
public static IAsyncEnumerable<ReadOnlyMemory<Byte>> EnumerateRealtimeEventsAsync(string sessionName, Action<EtwJsonConverterOptions> options, CancellationToken cancellationToken)
```

#### Parameters

`sessionName` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
The name of an already-running real-time ETW session (e.g., created via
 [EtwRealtimeSession.Create(String, Action&lt;EtwRealtimeSessionOptions&gt;)](./nefarius.utilities.etw.etwrealtimesession.md#createstring-actionetwrealtimesessionoptions) or `logman start`).

`options` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)><br>
Options to further tweak the parsing operation.

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>
Token that, when cancelled, stops trace processing and completes the enumeration.

#### Returns

An [IAsyncEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.iasyncenumerable-1)<T> of raw UTF-8 JSON buffers, one per event. Each buffer
 is a self-contained JSON object — `{"Event":{"Timestamp":…,"Properties":[{…}]}}` —
 with no outer array wrapper. The caller may concatenate or wrap items as needed.

#### Exceptions

[ArgumentNullException](https://learn.microsoft.com/dotnet/api/system.argumentnullexception)<br>
`sessionName` is `null`.

[ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception)<br>
`sessionName` is empty or whitespace.

[EtwOpenTraceException](./nefarius.utilities.etw.exceptions.etwopentraceexception.md)<br>
The session could not be opened by the ETW API.

**Remarks:**

Each [ReadOnlyMemory<byte>](https://learn.microsoft.com/dotnet/api/system.readonlymemory-1) is backed by a buffer rented from
 [ArrayPool&lt;T&gt;.Shared](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.shared). The buffer is valid only until the next iteration step; the library
 returns it to the pool when `MoveNextAsync` is called. Do not retain references to the memory
 across iterations.

Trace processing runs on a dedicated background thread. A bounded channel couples the producer to the
 consumer, applying natural backpressure. When cancelled, the trace handle is closed from the
 cancellation callback, causing `ProcessTrace` to return promptly rather than waiting for the
 next flush-timer tick.

WPP decoding in real-time mode requires a pre-built
 [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext) supplied via
 `options` — the file-based [EtwUtil.EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)](./nefarius.utilities.etw.etwutil.md#enumeratepdbreferencesienumerablestring-actionetwmetadatascanoptions) pre-scan cannot be
 applied to live sessions.

### <a id="methods-enumeratesessionnames"/>**EnumerateSessionNames()**

Returns the names of all currently running ETW trace sessions.

```csharp
public static IReadOnlyList<String> EnumerateSessionNames()
```

#### Returns

A read-only list of session names in the order reported by `QueryAllTracesW`.
 An empty list means the system has no active trace sessions.

#### Exceptions

[Win32Exception](https://learn.microsoft.com/dotnet/api/system.componentmodel.win32exception)<br>
`QueryAllTracesW` returned a non-zero, non-`ERROR_MORE_DATA` error code, or
 the system reports more than 256 concurrent sessions and the buffer cannot be grown further.

**Remarks:**

The Windows default maximum is 64 concurrent sessions. When the registry key
 `EtwMaxLoggers` raises the limit above 64, this method retries first with
 capacity 128 and then with capacity 256 before giving up.

### <a id="methods-stoporphansession"/>**StopOrphanSession(String)**

Stops a real-time ETW session by name, ignoring the error if no session with that name exists.
 Use this at application startup to clean up sessions left behind by a previous crash.

```csharp
public static void StopOrphanSession(string sessionName)
```

#### Parameters

`sessionName` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
Name of the orphaned session to stop.

#### Exceptions

[ArgumentNullException](https://learn.microsoft.com/dotnet/api/system.argumentnullexception)<br>
`sessionName` is `null`.

[ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception)<br>
`sessionName` is empty or whitespace.
