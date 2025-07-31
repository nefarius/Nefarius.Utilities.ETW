using System.Collections.ObjectModel;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Benchmarks;

[MemoryDiagnoser(false)]
public class EtlParserBenchmarks
{
    [GlobalSetup]
    public void Setup()
    {
    }

    [Benchmark]
    public IReadOnlyList<TraceMessageFormat> TmfFileParserBenchmark()
    {
        return Tests.Shared.ExtractFromFormatFiles();
    }

    [Benchmark]
    public ReadOnlyCollection<TraceMessageFormat> PdbFileParserBenchmark()
    {
        return Tests.Shared.ExtractFromSymbolFiles();
    }

    [Benchmark]
    public bool BthPs3EtlTraceDecodingBenchmark()
    {
        return Tests.Shared.BthPs3EtlTraceDecoding();
    }

    [Benchmark]
    public bool DsHidMiniEtlTraceDecodingBenchmark()
    {
        return Tests.Shared.DsHidMiniEtlTraceDecoding();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Summary summary = BenchmarkRunner.Run<EtlParserBenchmarks>();
    }
}