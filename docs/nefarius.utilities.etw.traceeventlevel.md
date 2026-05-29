# TraceEventLevel

Namespace: Nefarius.Utilities.ETW

Specifies the verbosity level of events delivered from a provider to a realtime ETW session.
 Values match the `TRACE_LEVEL_*` constants defined in `evntprov.h`.

```csharp
public enum TraceEventLevel
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [TraceEventLevel](./nefarius.utilities.etw.traceeventlevel.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| None | 0 | No tracing. The provider is effectively disabled. |
| Critical | 1 | Abnormal exit or termination events. |
| Error | 2 | Severe errors that need logging. |
| Warning | 3 | Warnings such as resource allocation failures. |
| Information | 4 | Non-error informational events, such as entry and exit points. |
| Verbose | 5 | Detailed trace events from intermediate processing steps. |
