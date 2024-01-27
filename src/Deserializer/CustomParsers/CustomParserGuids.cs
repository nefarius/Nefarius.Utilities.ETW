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

    public static Guid KernelStackWalkGuid = new("def2fe46-7bd6-4b80-bd94-f57fe20d0ce3");
}