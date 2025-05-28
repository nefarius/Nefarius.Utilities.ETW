using System.Collections.ObjectModel;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;
using Nefarius.Utilities.ETW.Tests;

namespace Nefarius.Utilities.ETW.Benchmarks;

[MemoryDiagnoser(false)]
public class EtlParserBenchmarks
{
    [GlobalSetup]
    public void Setup()
    {
    }

    /*[Benchmark]
    public IReadOnlyList<TraceMessageFormat> TmfFileParserBenchmark()
    {
        return Shared.ExtractFromFormatFiles();
    }*/

    [Benchmark]
    public ReadOnlyCollection<TraceMessageFormat> PdbFileParserBenchmark()
    {
        return Shared.ExtractFromSymbolFiles();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Summary summary = BenchmarkRunner.Run<EtlParserBenchmarks>();
    }
}