namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

/// <summary>
///     Much of this knowledge has been acquired thanks to:
///     https://github.com/Microsoft/perfview/blob/master/src/TraceEvent/Parsers/kerneltraceeventparser.cs.base
///     https://github.com/Microsoft/perfview/blob/master/src/TraceEvent/Parsers/SymbolTraceEventParser.cs
/// </summary>
internal static class CustomParserGuids
{
    public static Guid KernelTraceControlImageIdGuid = new("b3e675d7-2554-4f18-830b-2762732560de");

    public static Guid KernelTraceControlMetaDataGuid = new("bbccf6c1-6cd1-48c4-80ff-839482e37671");

    public static Guid EventTraceGuid = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");

    /// <summary>
    ///     MOF provider TDH reports for the <c>MSNT_SystemTrace</c> classes. The on-disk header group is still
    ///     <see cref="EventTraceGuid" />.
    /// </summary>
    public static Guid SystemTraceControlGuid = new("9e814aad-3204-11d2-9a82-006008a86939");

    public static Guid KernelStackWalkGuid = new("def2fe46-7bd6-4b80-bd94-f57fe20d0ce3");
}