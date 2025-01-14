namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a Program DataBase meta-object extracted from the provided trace.
/// </summary>
public readonly struct PdbMetaData
{
    public required string PdbName { get; init; }

    public required Guid Guid { get; init; }

    public required int Age { get; init; }

    public string IndexPrefix
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(PdbName);

            string filename = Path.GetFileName(PdbName).ToLowerInvariant();

            return $"{filename}/{Guid.ToString("N").ToUpperInvariant()}{Age}/{filename}";
        }
    }
}