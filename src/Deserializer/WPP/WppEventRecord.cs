namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal readonly unsafe struct WppEventRecord(EVENT_RECORD* record)
{
    private readonly EVENT_RECORD* _record = record;
}