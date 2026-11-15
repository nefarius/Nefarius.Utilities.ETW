using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     WPP decoding context used to extract TMF information from resources like <c>.PDB</c> or <c>.TMF</c> files.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public sealed class DecodingContext
{
    private readonly object _updateLock = new();

    private volatile State _state;

    /// <summary>
    ///     New decoding context instance.
    /// </summary>
    /// <param name="decodingTypes">One or more <see cref="DecodingContextType" />s to look up decoding information in.</param>
    public DecodingContext(params IList<DecodingContextType> decodingTypes)
    {
        ArgumentNullException.ThrowIfNull(decodingTypes);
        _state = State.Build(decodingTypes.ToList());
    }

    /// <summary>
    ///     The deduplicated union of all WPP trace control GUIDs (= ETW provider GUIDs) across every
    ///     underlying <see cref="DecodingContextType" />.  Each GUID corresponds to one
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> declaration found in the loaded PDB files.
    ///     The collection is empty when the context was built from TMF files only.
    /// </summary>
    public IReadOnlyCollection<Guid> ProviderGuids => _state.ProviderGuids;

    internal TraceMessageFormat? GetTraceMessageFormatFor(Guid? messageGuid, int id)
    {
        if (messageGuid == null)
        {
            return null;
        }

        TraceMessageFormatLookupKey key = new(messageGuid.Value, id);

        return _state.Lookup.GetValueOrDefault(key);
    }

    /// <summary>
    ///     Returns the friendly TMC control name (e.g. <c>DsHidMiniTraceGuid</c>) for the given message format,
    ///     or <see langword="null" /> if no unambiguous override is available (TMF-only context, or a PDB with
    ///     zero / multiple control GUIDs).  Used by <see cref="WppEventRecord" /> when provider-name rewriting
    ///     is enabled.
    /// </summary>
    internal string? GetWppProviderNameOverride(TraceMessageFormat format)
    {
        TraceMessageFormatLookupKey key = new(format.MessageGuid, format.Id);
        return _state.ProviderNameOverrides.GetValueOrDefault(key);
    }

    /// <summary>
    ///     Creates a new <see cref="DecodingContext" /> instance with additionally provided <see cref="DecodingContextType" />
    ///     s.
    /// </summary>
    /// <param name="additionalDecodingTypes">
    ///     One or more <see cref="DecodingContextType" />s to be added to this instances'
    ///     types.
    /// </param>
    /// <returns>The extended <see cref="DecodingContext" /> instance.</returns>
    public DecodingContext ExtendWith(params IList<DecodingContextType> additionalDecodingTypes)
    {
        return new DecodingContext(_state.Types.Concat(additionalDecodingTypes).ToList());
    }

    /// <summary>
    ///     Atomically adds decoding sources to this instance, so decoders sharing it see them on later lookups.
    /// </summary>
    /// <remarks>
    ///     Safe to call concurrently with lookups. The update is all-or-nothing: when the new sources declare a
    ///     message that conflicts with an already loaded, different format, nothing is added and the current
    ///     contents stay intact.
    /// </remarks>
    /// <param name="additionalDecodingTypes">The sources to add.</param>
    /// <returns><see langword="true" /> when the sources were added; <see langword="false" /> when rejected.</returns>
    public bool TryExtend(params IList<DecodingContextType> additionalDecodingTypes)
    {
        ArgumentNullException.ThrowIfNull(additionalDecodingTypes);

        lock (_updateLock)
        {
            State current = _state;
            Dictionary<TraceMessageFormatLookupKey, TraceMessageFormat> seen = new(current.Lookup);

            foreach (TraceMessageFormat format in additionalDecodingTypes.SelectMany(t => t.TraceMessageFormats))
            {
                TraceMessageFormatLookupKey key = new(format.MessageGuid, format.Id);
                if (seen.TryGetValue(key, out TraceMessageFormat? existing))
                {
                    if (!IsSameFormat(existing, format))
                    {
                        // Same lookup key but a different message: refuse the whole update.
                        return false;
                    }

                    continue;
                }

                seen.Add(key, format);
            }

            _state = State.Build(current.Types.Concat(additionalDecodingTypes).ToList());
            return true;
        }
    }

    private static bool IsSameFormat(TraceMessageFormat a, TraceMessageFormat b) =>
        a.MessageFormat == b.MessageFormat && a.Provider == b.Provider && a.Function == b.Function &&
        a.Opcode == b.Opcode && a.Level == b.Level && a.Flags == b.Flags &&
        SameParameters(a.FunctionParameters, b.FunctionParameters);

    /// <summary>Parameter order is part of the schema: a different type at the same position reads the payload differently.</summary>
    private static bool SameParameters(IReadOnlyList<FunctionParameter> a, IReadOnlyList<FunctionParameter> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            FunctionParameter left = a[i];
            FunctionParameter right = b[i];
            if (left.Expression != right.Expression || left.Type != right.Type || left.Index != right.Index ||
                left.EnumTypeName != right.EnumTypeName ||
                !SameMap(left.ListItems, right.ListItems) || !SameMap(left.EnumValues, right.EnumValues))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameMap<TKey, TValue>(IReadOnlyDictionary<TKey, TValue>? a, IReadOnlyDictionary<TKey, TValue>? b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (KeyValuePair<TKey, TValue> pair in a)
        {
            if (!b.TryGetValue(pair.Key, out TValue? value) || !EqualityComparer<TValue>.Default.Equals(value, pair.Value))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class State
    {
        private State(
            ReadOnlyCollection<DecodingContextType> types,
            ReadOnlyDictionary<TraceMessageFormatLookupKey, TraceMessageFormat> lookup,
            IReadOnlyCollection<Guid> providerGuids,
            IReadOnlyDictionary<TraceMessageFormatLookupKey, string> providerNameOverrides)
        {
            Types = types;
            Lookup = lookup;
            ProviderGuids = providerGuids;
            ProviderNameOverrides = providerNameOverrides;
        }

        public ReadOnlyCollection<DecodingContextType> Types { get; }

        public ReadOnlyDictionary<TraceMessageFormatLookupKey, TraceMessageFormat> Lookup { get; }

        public IReadOnlyCollection<Guid> ProviderGuids { get; }

        /// <summary>
        ///     Maps (MessageGuid, Id) -> friendly control name for PDB sources that declare exactly one
        ///     unambiguous <c>WPP_DEFINE_CONTROL_GUID</c>.
        /// </summary>
        public IReadOnlyDictionary<TraceMessageFormatLookupKey, string> ProviderNameOverrides { get; }

        public static State Build(List<DecodingContextType> decodingTypes)
        {
            ReadOnlyCollection<DecodingContextType> types = decodingTypes.AsReadOnly();

            ReadOnlyDictionary<TraceMessageFormatLookupKey, TraceMessageFormat> lookup = types
                .SelectMany(t => t.TraceMessageFormats)
                .Distinct()
                .ToDictionary(key => new TraceMessageFormatLookupKey(key.MessageGuid, key.Id), value => value)
                .AsReadOnly();

            // Aggregate the real WPP control GUIDs across all underlying sources.
            // TMF-only sources return an empty enumerable from ProviderGuids so they are
            // silently skipped; only PdbFileDecodingContextType contributes entries.
            Guid[] providerGuids = types
                .SelectMany(t => t.ProviderGuids)
                .Distinct()
                .ToArray();

            // Only PDB sources with exactly one non-empty control name contribute entries;
            // zero or multiple controls are ambiguous and are skipped silently.
            Dictionary<TraceMessageFormatLookupKey, string> overrides = [];

            foreach (DecodingContextType t in types)
            {
                if (t is not PdbFileDecodingContextType pdb)
                {
                    continue;
                }

                IReadOnlyCollection<WppTraceControl> controls = pdb.WppTraceControls;

                if (controls.Count != 1)
                {
                    continue;
                }

                string controlName = controls.Single().Name;

                if (string.IsNullOrWhiteSpace(controlName))
                {
                    continue;
                }

                foreach (TraceMessageFormat fmt in pdb.TraceMessageFormats)
                {
                    overrides.TryAdd(new TraceMessageFormatLookupKey(fmt.MessageGuid, fmt.Id), controlName);
                }
            }

            return new State(types, lookup, providerGuids, overrides.AsReadOnly());
        }
    }
    private record TraceMessageFormatLookupKey(Guid MessageGuid, int Id);
}