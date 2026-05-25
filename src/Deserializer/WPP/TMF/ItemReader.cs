using System.Collections.ObjectModel;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Maps <see cref="ItemType" />s to the appropriate <see cref="EventRecordReader" /> reader method.
/// </summary>
internal static class ItemReader
{
    public static readonly ReadOnlyDictionary<ItemType, Func<EventRecordReader, object>> Readers =
        new Dictionary<ItemType, Func<EventRecordReader, object>>
        {
            #region basic arithmetic types

            [ItemType.ItemLong] = r => r.ReadInt32(),
            [ItemType.ItemLongLong] = r => r.ReadInt64(),
            [ItemType.ItemLongLongX] = r => r.ReadInt64(),
            [ItemType.ItemLongLongXX] = r => r.ReadInt64(),
            [ItemType.ItemLongLongO] = r => r.ReadInt64(),
            [ItemType.ItemULongLong] = r => r.ReadUInt64(),
            [ItemType.ItemDouble] = r => r.ReadDouble(),
            [ItemType.ItemChar] = r => r.ReadInt8(),
            [ItemType.ItemUChar] = r => r.ReadUInt8(),
            [ItemType.ItemShort] = r => r.ReadInt16(),

            #endregion

            #region arch dependent types

            [ItemType.ItemPtr] = r => r.ReadPointer(),
            [ItemType.ItemGuid] = r => r.ReadGuid(),

            #endregion

            #region Complex types

            [ItemType.ItemString] = r => r.ReadAnsiString(),
            [ItemType.ItemRString] = r => r.ReadAnsiString(),
            [ItemType.ItemRWString] = r => r.ReadUnicodeString(),
            [ItemType.ItemWString] = r => r.ReadUnicodeString(),
            [ItemType.ItemPString] = r => r.ReadCountedAnsiString(),
            [ItemType.ItemPWString] = r => r.ReadCountedString(),
            [ItemType.ItemSid] = r => r.ReadSid(),
            [ItemType.ItemHexDump] = r => r.ReadHexDump(),

            #endregion

            #region enumeration types

            [ItemType.ItemListByte]   = r => r.ReadUInt8(),
            [ItemType.ItemListShort]  = r => r.ReadInt16(),
            [ItemType.ItemListLong]   = r => r.ReadInt32(),

            [ItemType.ItemSetByte]    = r => r.ReadUInt8(),
            [ItemType.ItemSetShort]   = r => r.ReadUInt16(),
            [ItemType.ItemSetLong]    = r => r.ReadUInt32(),

            [ItemType.ItemEnum]       = r => r.ReadUInt32(),
            [ItemType.ItemFlagsEnum]  = r => r.ReadUInt32(),

            #endregion

            #region special formats

            [ItemType.ItemNTSTATUS]   = r => r.ReadUInt32(),
            [ItemType.ItemWINERROR]   = r => r.ReadUInt32(),
            [ItemType.ItemHRESULT]    = r => r.ReadInt32(),

            [ItemType.ItemNDIS_STATUS] = r => r.ReadUInt32(),
            [ItemType.ItemNDIS_OID]    = r => r.ReadUInt32(),

            [ItemType.ItemIPAddr]     = r => r.ReadUInt32(),
            [ItemType.ItemPort]       = r => r.ReadUInt16(),

            [ItemType.ItemTimestamp]  = r => r.ReadInt64(),
            [ItemType.ItemTimeDelta]  = r => r.ReadInt64(),
            [ItemType.ItemWaitTime]   = r => r.ReadInt64(),

            [ItemType.ItemCLSID]      = r => r.ReadGuid(),
            [ItemType.ItemLIBID]      = r => r.ReadGuid(),
            [ItemType.ItemIID]        = r => r.ReadGuid(),

            [ItemType.ItemChar4]      = r => r.ReadInt32()

            #endregion
        }.AsReadOnly();
}