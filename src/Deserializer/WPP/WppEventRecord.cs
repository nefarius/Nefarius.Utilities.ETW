namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal readonly unsafe struct WppEventRecord(EVENT_RECORD* record, DecodingContext decodingContext)
{
    public void Decode()
    {
        uint size = 0;
        uint ret = PInvoke.TdhGetWppMessage(decodingContext.Handle, record, &size, null);

        byte* messageBuffer = stackalloc byte[(int)size];

        ret = PInvoke.TdhGetWppMessage(decodingContext.Handle, record, &size, messageBuffer);

        string message = new((char*)messageBuffer);
    }
}