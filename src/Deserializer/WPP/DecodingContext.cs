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