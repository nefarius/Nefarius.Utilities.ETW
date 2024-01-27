namespace Nefarius.Utilities.ETW.Deserializer;

public sealed class MapInformation
{
    private readonly Dictionary<uint, string> mapOfValues;

    internal MapInformation(string name, Dictionary<uint, string> mapOfValues)
    {
        Name = name;
        this.mapOfValues = mapOfValues;
    }

    public string Name { get; private set; }

    public string this[uint i] => mapOfValues[i];
}