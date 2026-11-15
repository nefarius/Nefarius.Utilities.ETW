using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     An <see cref="IEtwWriter" /> that builds <see cref="EtwEvent" /> objects and hands each completed event to
///     <see cref="Sink" />.
/// </summary>
internal sealed class EtwStructuredWriter : IEtwWriter
{
    private readonly Stack<Container> _stack = new();
    private EtwEvent? _event;

    /// <summary>
    ///     Receives each completed event. Must be set before decoding.
    /// </summary>
    public Action<EtwEvent>? Sink { get; set; }

    public void WriteEventBegin(EventMetadata metadata, RuntimeEventMetadata runtimeMetadata)
    {
        // A previous event that never ended (an exception mid-decode) is discarded.
        _stack.Clear();

        ulong[]? stacks = runtimeMetadata.GetStacks(out ulong matchId);
        _event = new EtwEvent
        {
            Timestamp = runtimeMetadata.Timestamp,
            ProviderId = metadata.ProviderGuid,
            Name = metadata.Name,
            Id = metadata.Id,
            Version = metadata.Version,
            ProcessId = runtimeMetadata.ProcessId,
            ThreadId = runtimeMetadata.ThreadId,
            ProcessorNumber = runtimeMetadata.ProcessorNumber,
            ActivityId = runtimeMetadata.ActivityId,
            RelatedActivityId = runtimeMetadata.RelatedActivityId,
            Flags = runtimeMetadata.Flags,
            Channel = runtimeMetadata.Channel,
            Level = runtimeMetadata.Level,
            Opcode = runtimeMetadata.Opcode,
            Task = runtimeMetadata.Task,
            Keyword = runtimeMetadata.Keyword,
            StackMatchId = matchId,
            Stacks = stacks
        };

        _stack.Push(new Container(isArray: false));
    }

    public void WriteEventEnd()
    {
        if (_event is null || _stack.Count == 0)
        {
            return;
        }

        _event.Properties = _stack.Pop().ToValue();
        _stack.Clear();
        EtwEvent completed = _event;
        _event = null;
        Sink?.Invoke(completed);
    }

    public void WriteStructBegin() => _stack.Push(new Container(isArray: false));

    public void WriteStructEnd() => Pop();

    public void WritePropertyBegin(PropertyMetadata metadata)
    {
        if (_stack.Count > 0)
        {
            _stack.Peek().PendingName = metadata.Name;
        }
    }

    public void WritePropertyEnd() { }

    public void WriteArrayBegin() => _stack.Push(new Container(isArray: true));

    public void WriteArrayEnd() => Pop();

    public void WriteAnsiString(string value) => Add(EtwValue.FromString(value));

    public void WriteUnicodeString(string? value) => Add(EtwValue.FromString(value));

    public void WriteInt8(sbyte value) => Add(EtwValue.FromInt64(value));

    public void WriteUInt8(byte value) => Add(EtwValue.FromUInt64(value));

    public void WriteInt16(short value) => Add(EtwValue.FromInt64(value));

    public void WriteUInt16(ushort value) => Add(EtwValue.FromUInt64(value));

    public void WriteInt32(int value) => Add(EtwValue.FromInt64(value));

    public void WriteUInt32(uint value) => Add(EtwValue.FromUInt64(value));

    public void WriteInt64(long value) => Add(EtwValue.FromInt64(value));

    public void WriteUInt64(ulong value) => Add(EtwValue.FromUInt64(value));

    public void WriteFloat(float value) => Add(EtwValue.FromDouble(value));

    public void WriteDouble(double value) => Add(EtwValue.FromDouble(value));

    public void WriteBoolean(bool value) => Add(EtwValue.FromBoolean(value));

    public void WriteNull() => Add(EtwValue.Null);

    public void WriteBinary(byte[] value) => Add(EtwValue.FromBinary(value));

    public void WriteGuid(Guid value) => Add(EtwValue.FromGuid(value));

    public void WritePointer(ulong value) => Add(EtwValue.FromUInt64(value));

    public void WriteFileTime(DateTime value) => Add(EtwValue.FromDateTime(value));

    public void WriteSystemTime(DateTime value) => Add(EtwValue.FromDateTime(value));

    public void WriteSid(string value) => Add(EtwValue.FromString(value));

    public void WriteUnicodeChar(char value) => Add(EtwValue.FromUInt64(value));

    public void WriteAnsiChar(char value) => Add(EtwValue.FromUInt64(value));

    public void WriteHexDump(byte[] value) => Add(EtwValue.FromBinary(value));

    public void WriteWbemSid(string value) => Add(EtwValue.FromString(value));

    private void Pop()
    {
        // Never pop the event's root container; an unbalanced end is ignored.
        if (_stack.Count > 1)
        {
            EtwValue value = _stack.Pop().ToValue();
            Add(value);
        }
    }

    private void Add(EtwValue value)
    {
        if (_stack.Count > 0)
        {
            _stack.Peek().Add(value);
        }
    }

    private sealed class Container
    {
        private readonly List<EtwValue>? _items;
        private readonly List<KeyValuePair<string, EtwValue>>? _members;

        public Container(bool isArray)
        {
            if (isArray)
            {
                _items = [];
            }
            else
            {
                _members = [];
            }
        }

        public string? PendingName { get; set; }

        public void Add(EtwValue value)
        {
            if (_items is not null)
            {
                _items.Add(value);
            }
            else
            {
                _members!.Add(new KeyValuePair<string, EtwValue>(PendingName ?? string.Empty, value));
            }
        }

        public EtwValue ToValue() => _items is not null ? EtwValue.FromArray(_items) : EtwValue.FromObject(_members!);
    }
}
