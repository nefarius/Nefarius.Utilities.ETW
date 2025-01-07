namespace Nefarius.Utilities.ETW.Deserializer.WPP;

public readonly struct EventPropertyMeta
{
    public required string Name { get; init; }

    public required int Size { get; init; }
}