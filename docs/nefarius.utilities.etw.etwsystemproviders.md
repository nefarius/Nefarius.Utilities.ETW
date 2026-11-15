# EtwSystemProviders

Namespace: Nefarius.Utilities.ETW

Well-known kernel (system) provider GUIDs for sessions created with
 [EtwRealtimeSessionOptions.SystemLogger](./nefarius.utilities.etw.etwrealtimesessionoptions.md#systemlogger).

```csharp
public static class EtwSystemProviders
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwSystemProviders](./nefarius.utilities.etw.etwsystemproviders.md)

## Fields

### <a id="fields-process"/>**Process**

The system process provider (`SystemProcessProvider`); keyword `0x1` (`SYSTEM_PROCESS_KW_GENERAL`) enables process lifetime events.

```csharp
public static Guid Process;
```
