<#
.SYNOPSIS
    Deterministic ETW event producer used to generate the ETL fixtures in tests/traces.

.DESCRIPTION
    Mode TraceLogging : self-describing (TraceLogging) events via EventSource.Write<T>.
    Mode Manifest     : manifest based EventSource events (manifest is delivered in-band), including an enum value
                        map and a [Flags] bit map.
    Mode Bulk         : many TraceLogging events with compressible and incompressible payloads, to fill several
                        logger buffers (used for the compressed-mode fixtures).

    Runs on Windows PowerShell 5.1 (x64 and x86) and PowerShell 7.
#>
param(
    [Parameter(Mandatory)][ValidateSet('TraceLogging', 'Manifest', 'Bulk')][string]$Mode,
    [int]$Count = 400
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Diagnostics.Tracing;

namespace Nefarius.Fixtures
{
    [EventSource(Name = "Nefarius-Fixture-TraceLogging", Guid = "6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b01")]
    public sealed class TraceLoggingSource : EventSource
    {
        public static readonly TraceLoggingSource Log = new TraceLoggingSource();

        private static EventSourceOptions Info(long keywords = 0)
        {
            return new EventSourceOptions { Level = EventLevel.Informational, Keywords = (EventKeywords)keywords };
        }

        public void Scalars()
        {
            Write("Scalars", Info(), new
            {
                I8 = (sbyte)-5, U8 = (byte)250, I16 = (short)-300, U16 = (ushort)60000,
                I32 = int.MinValue, U32 = uint.MaxValue, I64 = long.MinValue, U64 = ulong.MaxValue,
                F32 = 1.5f, F64 = -2.25d, B = true, C = 'x', Text = "hello world",
                G = new Guid("00112233-4455-6677-8899-aabbccddeeff"),
                Stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
                Sentinel = 0xCAFEBABE
            });
        }

        public void Arrays()
        {
            Write("Arrays", Info(), new
            {
                Ints = new[] { 1, 2, 3, 4 }, Bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                Empty = new int[0], Sentinel = 0xCAFEBABE
            });
        }

        public void Nested()
        {
            Write("Nested", Info(), new
            {
                Outer = new { Name = "outer", Inner = new { Value = 42, Tag = "inner" }, After = 7L },
                Sentinel = 0xCAFEBABE
            });
        }

        public void Bulk(int index, string compressible, byte[] random)
        {
            Write("Bulk", Info(1), new { Index = index, Compressible = compressible, Random = random });
        }
    }

    public enum Color { Red = 1, Green = 2, Blue = 4 }

    [Flags]
    public enum Access { None = 0, Read = 1, Write = 2, Execute = 4 }

    [EventSource(Name = "Nefarius-Fixture-Manifest", Guid = "6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b02")]
    public sealed class ManifestSource : EventSource
    {
        public static readonly ManifestSource Log = new ManifestSource();

        [Event(1, Level = EventLevel.Informational, Message = "Basic {0} {1}")]
        public void Basic(int number, string text) { WriteEvent(1, number, text); }

        [Event(2, Level = EventLevel.Informational)]
        public void Wide(long value, bool flag, double ratio) { WriteEvent(2, value, flag, ratio); }

        [Event(3, Level = EventLevel.Informational)]
        public void Mapped(Color color, Access access, int sentinel) { WriteEvent(3, (int)color, (int)access, sentinel); }

        [Event(4, Level = EventLevel.Informational)]
        public void Blob(byte[] data, int sentinel) { WriteEvent(4, data, sentinel); }

        [Event(5, Level = EventLevel.Informational)]
        public void Identity(Guid id, string name) { WriteEvent(5, id, name); }
    }
}
"@

switch ($Mode) {
    'TraceLogging' {
        $log = [Nefarius.Fixtures.TraceLoggingSource]::Log
        1..3 | ForEach-Object { $log.Scalars(); $log.Arrays(); $log.Nested() }
    }
    'Manifest' {
        $log = [Nefarius.Fixtures.ManifestSource]::Log
        $log.Basic(7, 'first')
        $log.Wide([long]::MaxValue, $true, 3.5)
        $log.Mapped([Nefarius.Fixtures.Color]::Green, [Nefarius.Fixtures.Access]'Read,Execute', 99)
        $log.Mapped([Nefarius.Fixtures.Color]::Blue, [Nefarius.Fixtures.Access]'Write', 100)
        $log.Blob([byte[]](1, 2, 3, 4, 5), 101)
        $log.Identity([guid]'00112233-4455-6677-8899-aabbccddeeff', 'identity')
        $log.Basic(8, 'second')
    }
    'Bulk' {
        $log = [Nefarius.Fixtures.TraceLoggingSource]::Log
        $filler = 'The quick brown fox jumps over the lazy dog. ' * 4
        # A fixed seed keeps the incompressible payloads reproducible between runs.
        $rng = New-Object System.Random 12345
        for ($i = 0; $i -lt $Count; $i++) {
            $random = New-Object byte[] 96
            $rng.NextBytes($random)
            $log.Bulk($i, $filler, $random)
        }
    }
}

# Give the session time to drain the in-memory buffers before the caller stops it.
Start-Sleep -Milliseconds 500
