# RawEtwProtocolException

Namespace: Nefarius.Utilities.ETW.Relay

Thrown when relay input violates the protocol or the configured [RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md).

```csharp
public sealed class RawEtwProtocolException : System.Exception, System.Runtime.Serialization.ISerializable
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [Exception](https://learn.microsoft.com/dotnet/api/system.exception) → [RawEtwProtocolException](./nefarius.utilities.etw.relay.rawetwprotocolexception.md)<br>
Implements [ISerializable](https://learn.microsoft.com/dotnet/api/system.runtime.serialization.iserializable)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-data"/>**Data**

```csharp
public IDictionary Data { get; }
```

#### Property Value

[IDictionary](https://learn.microsoft.com/dotnet/api/system.collections.idictionary)<br>

### <a id="properties-error"/>**Error**

The reason the input was rejected.

```csharp
public RawEtwProtocolError Error { get; }
```

#### Property Value

[RawEtwProtocolError](./nefarius.utilities.etw.relay.rawetwprotocolerror.md)<br>

### <a id="properties-helplink"/>**HelpLink**

```csharp
public string HelpLink { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-hresult"/>**HResult**

```csharp
public int HResult { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-innerexception"/>**InnerException**

```csharp
public Exception InnerException { get; }
```

#### Property Value

[Exception](https://learn.microsoft.com/dotnet/api/system.exception)<br>

### <a id="properties-message"/>**Message**

```csharp
public string Message { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-source"/>**Source**

```csharp
public string Source { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-stacktrace"/>**StackTrace**

```csharp
public string StackTrace { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-targetsite"/>**TargetSite**

```csharp
public MethodBase TargetSite { get; }
```

#### Property Value

[MethodBase](https://learn.microsoft.com/dotnet/api/system.reflection.methodbase)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwProtocolException(RawEtwProtocolError, String)**

Creates the exception.

```csharp
public RawEtwProtocolException(RawEtwProtocolError error, string message)
```

#### Parameters

`error` [RawEtwProtocolError](./nefarius.utilities.etw.relay.rawetwprotocolerror.md)<br>

`message` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="constructors-.ctor"/>**RawEtwProtocolException(RawEtwProtocolError, String, Exception)**

Creates the exception with the failure that caused it.

```csharp
public RawEtwProtocolException(RawEtwProtocolError error, string message, Exception innerException)
```

#### Parameters

`error` [RawEtwProtocolError](./nefarius.utilities.etw.relay.rawetwprotocolerror.md)<br>

`message` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`innerException` [Exception](https://learn.microsoft.com/dotnet/api/system.exception)<br>
