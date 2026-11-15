# RawEtwRelayOptions

Namespace: Nefarius.Utilities.ETW.Relay

Settings of a relay decoding session that are independent of how events are decoded
 (see [EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md) for that).

```csharp
public sealed class RawEtwRelayOptions
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwRelayOptions](./nefarius.utilities.etw.relay.rawetwrelayoptions.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-limits"/>**Limits**

The limits enforced on incoming frames. Defaults to [RawEtwRelayLimits.Default](./nefarius.utilities.etw.relay.rawetwrelaylimits.md#default).

```csharp
public RawEtwRelayLimits Limits { get; set; }
```

#### Property Value

[RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>

### <a id="properties-onend"/>**OnEnd**

Invoked when the session's end frame is accepted. Retries of an end frame already processed are not reported.

```csharp
public Action OnEnd { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action)<br>

### <a id="properties-onhello"/>**OnHello**

Invoked when the session's hello frame is accepted.

```csharp
public Action<RawEtwHello> OnHello { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[RawEtwHello](./nefarius.utilities.etw.relay.rawetwhello.md)><br>

### <a id="properties-onloss"/>**OnLoss**

Invoked when the producer reports dropped events.

```csharp
public Action<RawEtwLoss> OnLoss { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[RawEtwLoss](./nefarius.utilities.etw.relay.rawetwloss.md)><br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwRelayOptions()**

```csharp
public RawEtwRelayOptions()
```
