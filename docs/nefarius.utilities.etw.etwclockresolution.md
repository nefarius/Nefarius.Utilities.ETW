# EtwClockResolution

Namespace: Nefarius.Utilities.ETW

Clock source used to timestamp ETW events.
 Corresponds to the `ClientContext` field of `WNODE_HEADER`.

```csharp
public enum EtwClockResolution
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [ValueType](https://docs.microsoft.com/en-us/dotnet/api/system.valuetype) → [Enum](https://docs.microsoft.com/en-us/dotnet/api/system.enum) → [EtwClockResolution](./nefarius.utilities.etw.etwclockresolution.md)<br>
Implements [IComparable](https://docs.microsoft.com/en-us/dotnet/api/system.icomparable), [ISpanFormattable](https://docs.microsoft.com/en-us/dotnet/api/system.ispanformattable), [IFormattable](https://docs.microsoft.com/en-us/dotnet/api/system.iformattable), [IConvertible](https://docs.microsoft.com/en-us/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| QueryPerformanceCounter | 1 | Query Performance Counter — highest precision, correlated to wall clock. This is the recommended default. |
| SystemTime | 2 | System time in 100-nanosecond intervals since 1 January 1601. Lower overhead but coarser resolution than [EtwClockResolution.QueryPerformanceCounter](./nefarius.utilities.etw.etwclockresolution.md#queryperformancecounter). |
| CpuCycle | 3 | CPU cycle counter. Highest frequency but not correlated to wall-clock time and may not be comparable across processors. |
