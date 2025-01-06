using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal enum ItemType
{
    ItemListByte,
    ItemLong,
    ItemLongLong,
    ItemLongLongXX,
    ItemNTSTATUS,
    ItemPWString,
    ItemPtr,
    ItemString,
}

internal struct FunctionParameter
{
    public required string Expression { get; set; }

    public required ItemType Type { get; set; }

    public required int Index { get; set; }

    public IReadOnlyDictionary<int, string>? ListItems { get; set; }
}