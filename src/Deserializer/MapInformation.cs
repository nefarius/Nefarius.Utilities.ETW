namespace Nefarius.Utilities.ETW.Deserializer;

internal sealed class MapInformation
{
    private readonly Dictionary<uint, string> _mapOfValues;

    internal MapInformation(string name, Dictionary<uint, string> mapOfValues)
    {
        Name = name;
        this._mapOfValues = mapOfValues;
    }

    public string Name { get; private set; }

    public string this[uint i] => _mapOfValues[i];
}