namespace Nefarius.Utilities.ETW.Deserializer;

internal sealed class PropertyMetadata
{
    internal PropertyMetadata(_TDH_IN_TYPE inType, _TDH_OUT_TYPE outType, string name, bool isMapValue, bool isStruct,
        int childrenCount, MapInformation? map)
    {
        InType = inType;
        OutType = outType;
        Name = name;
        IsMapValue = isMapValue;
        IsStruct = isStruct;
        ChildrenCount = childrenCount;
        Map = map;
    }

    public _TDH_IN_TYPE InType { get; private set; }

    public _TDH_OUT_TYPE OutType { get; private set; }

    public bool IsMapValue { get; private set; }

    public bool IsStruct { get; private set; }

    public int ChildrenCount { get; private set; }

    public MapInformation? Map { get; private set; }

    public string Name { get; private set; }
}