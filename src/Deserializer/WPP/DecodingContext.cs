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
    private readonly ReadOnlyCollection<DecodingContextType> _decodingTypes;

    private readonly ReadOnlyDictionary<TraceMessageFormatLookupKey, TraceMessageFormat> _lookup;

    private readonly IReadOnlyCollection<Guid> _providerGuids;

    /// <summary>
    ///     Maps (MessageGuid, Id) -> friendly control name for PDB sources that declare exactly one
    ///     unambiguous <c>WPP_DEFINE_CONTROL_GUID</c>. Built lazily on first use.
    /// </summary>
    private readonly IReadOnlyDictionary<TraceMessageFormatLookupKey, string> _providerNameOverrides;

    /// <summary>
    ///     New decoding context instance.
    /// </summary>
    /// <param name="decodingTypes">One or more <see cref="DecodingContextType" />s to look up decoding information in.</param>
    public DecodingContext(params IList<DecodingContextType> decodingTypes)
    {
        ArgumentNullException.ThrowIfNull(decodingTypes);
        _decodingTypes = decodingTypes.AsReadOnly();

        _lookup = decodingTypes
            .SelectMany(t => t.TraceMessageFormats)
            .Distinct()
            .ToDictionary(key => new TraceMessageFormatLookupKey(key.MessageGuid, key.Id), value => value)
            .AsReadOnly();

        // Aggregate the real WPP control GUIDs across all underlying sources.
        // TMF-only sources return an empty enumerable from ProviderGuids so they are
        // silently skipped; only PdbFileDecodingContextType contributes entries.
        _providerGuids = _decodingTypes
            .SelectMany(t => t.ProviderGuids)
            .Distinct()
            .ToArray();

        // Build the provider-name override map. Only PDB sources with exactly one non-empty control name
        // contribute entries; zero or multiple controls are ambiguous and are skipped silently.
        Dictionary<TraceMessageFormatLookupKey, string> overrides = [];

        foreach (DecodingContextType t in _decodingTypes)
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

            if (string.IsNullOrEmpty(controlName))
            {
                continue;
            }

            foreach (TraceMessageFormat fmt in pdb.TraceMessageFormats)
            {
                TraceMessageFormatLookupKey key = new(fmt.MessageGuid, fmt.Id);
                overrides.TryAdd(key, controlName);
            }
        }

        _providerNameOverrides = overrides.AsReadOnly();
    }

    /// <summary>
    ///     The deduplicated union of all WPP trace control GUIDs (= ETW provider GUIDs) across every
    ///     underlying <see cref="DecodingContextType" />.  Each GUID corresponds to one
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> declaration found in the loaded PDB files.
    ///     The collection is empty when the context was built from TMF files only.
    /// </summary>
    public IReadOnlyCollection<Guid> ProviderGuids => _providerGuids;

    internal TraceMessageFormat? GetTraceMessageFormatFor(Guid? messageGuid, int id)
    {
        if (messageGuid == null)
        {
            return null;
        }

        TraceMessageFormatLookupKey key = new(messageGuid.Value, id);

        return _lookup.GetValueOrDefault(key);
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
        return _providerNameOverrides.GetValueOrDefault(key);
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
        return new DecodingContext(_decodingTypes.Concat(additionalDecodingTypes).ToList());
    }

    private record TraceMessageFormatLookupKey(Guid MessageGuid, int Id);
}