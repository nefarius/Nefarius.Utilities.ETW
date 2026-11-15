<#
.SYNOPSIS
    Regenerates the ETL fixtures under tests/traces/fixtures that exercise the managed reader and decoders.

.DESCRIPTION
    Requires an elevated session on Windows. Sessions are started with StartTrace/EnableTraceEx2 so that the
    logger mode (compressed, system logger, buffer size) is fully controlled. Producers live in ./producers.

    Outputs (into -OutDir):
      tracelogging-x64.etl       TraceLogging events from PowerShell 7 (x64)
      tracelogging-x86.etl       TraceLogging events from 32-bit Windows PowerShell (4-byte pointers)
      manifest-x64.etl           Manifest EventSource events with an enum value map and a [Flags] bit map
      bulk-uncompressed.etl      Many events across several logger buffers
      bulk-compressed.etl        The same workload with EVENT_TRACE_COMPRESSED_MODE
      kernel-classic.etl         classic kernel process events (SYSTEM_TRACE headers), first 1 MiB

    The result of a run is summarised in fixtures.manifest.txt (sizes and SHA-256 hashes); the files are not
    bit-identical between runs because they contain timestamps and process ids.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\traces\fixtures'),
    [string]$Pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe',
    [int]$BulkCount = 400
)

$ErrorActionPreference = 'Stop'
$producer = Join-Path $PSScriptRoot 'producers\EventSourceProducer.ps1'
$null = New-Item -ItemType Directory -Force -Path $OutDir
$OutDir = (Resolve-Path $OutDir).Path

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class FixtureEtw
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint StartTraceW(out ulong handle, string name, IntPtr props);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint ControlTraceW(ulong handle, string name, IntPtr props, uint code);
    [DllImport("advapi32.dll")]
    static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level,
        ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);

    const int PropsSize = 120;
    const int NameSize = 2048;

    public static IntPtr NewProps(string logger, string file, Guid sessionGuid, uint bufferKb, uint mode, uint flags)
    {
        int total = PropsSize + NameSize + 2048;
        IntPtr p = Marshal.AllocHGlobal(total);
        for (int i = 0; i < total; i++) Marshal.WriteByte(p, i, 0);
        Marshal.WriteInt32(p, 0, total);
        Marshal.Copy(sessionGuid.ToByteArray(), 0, IntPtr.Add(p, 24), 16);
        Marshal.WriteInt32(p, 40, 1);          // QPC clock
        Marshal.WriteInt32(p, 44, 0x20000);    // WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(p, 48, (int)bufferKb);
        Marshal.WriteInt32(p, 52, 4);          // minimum buffers
        Marshal.WriteInt32(p, 56, 16);         // maximum buffers
        Marshal.WriteInt32(p, 64, (int)mode);
        Marshal.WriteInt32(p, 72, (int)flags);
        Marshal.WriteInt32(p, 112, PropsSize + NameSize);
        Marshal.WriteInt32(p, 116, PropsSize);
        WriteString(p, PropsSize, logger);
        WriteString(p, PropsSize + NameSize, file);
        return p;
    }

    static void WriteString(IntPtr p, int offset, string s)
    {
        byte[] b = System.Text.Encoding.Unicode.GetBytes(s + "\0");
        Marshal.Copy(b, 0, IntPtr.Add(p, offset), b.Length);
    }

    public static ulong Start(string logger, IntPtr props)
    {
        ulong h;
        uint rc = StartTraceW(out h, logger, props);
        if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc, "StartTrace " + logger);
        return h;
    }

    public static void Enable(ulong h, Guid provider)
    {
        uint rc = EnableTraceEx2(h, ref provider, 1, 5, 0xFFFFFFFFFFFFFFFFUL, 0, 0, IntPtr.Zero);
        if (rc != 0) throw new System.ComponentModel.Win32Exception((int)rc, "EnableTraceEx2");
    }

    public static void Stop(string logger, IntPtr props)
    {
        ControlTraceW(0, logger, props, 1);
        Marshal.FreeHGlobal(props);
    }
}
"@

$SystemTraceControl = [guid]'9e814aad-3204-11d2-9a82-006008a86939'
$Sequential = 0x1
$Compressed = 0x04000000

function Invoke-Capture {
    param(
        [string]$Name, [string]$File, [Guid[]]$Providers, [uint32]$Mode = $Sequential, [uint32]$Flags = 0,
        [string]$Logger = "NefariusFixture-$Name", [Guid]$SessionGuid = [guid]::Empty, [uint32]$BufferKb = 64,
        [scriptblock]$Workload
    )

    $path = Join-Path $OutDir $File
    Remove-Item $path -ErrorAction SilentlyContinue
    $props = [FixtureEtw]::NewProps($Logger, $path, $SessionGuid, $BufferKb, $Mode, $Flags)
    $handle = [FixtureEtw]::Start($Logger, $props)
    try {
        foreach ($provider in $Providers) { [FixtureEtw]::Enable($handle, $provider) }
        & $Workload
    }
    finally {
        [FixtureEtw]::Stop($Logger, $props)
    }

    "{0,-28} {1,10:N0} bytes" -f $File, (Get-Item $path).Length
}

function Invoke-Producer([string]$Exe, [string]$ProducerMode, [string[]]$Extra = @()) {
    & $Exe -NoProfile -ExecutionPolicy Bypass -File $producer -Mode $ProducerMode @Extra | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$Exe producer $ProducerMode failed ($LASTEXITCODE)" }
}

$traceLogging = [guid]'6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b01'
$manifest = [guid]'6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b02'
$winPs64 = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
$winPs32 = "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"

Invoke-Capture 'tl64' 'tracelogging-x64.etl' @($traceLogging) -Workload { Invoke-Producer $Pwsh 'TraceLogging' }
Invoke-Capture 'tl32' 'tracelogging-x86.etl' @($traceLogging) -Workload { Invoke-Producer $winPs32 'TraceLogging' }
Invoke-Capture 'man64' 'manifest-x64.etl' @($manifest) -Workload { Invoke-Producer $winPs64 'Manifest' }

$bulk = { Invoke-Producer $Pwsh 'Bulk' @('-Count', $BulkCount) }
Invoke-Capture 'bulk' 'bulk-uncompressed.etl' @($traceLogging) -Workload $bulk
Invoke-Capture 'bulkc' 'bulk-compressed.etl' @($traceLogging) -Mode ($Sequential -bor $Compressed) -Workload $bulk

# Classic kernel events (SYSTEM_TRACE / PERFINFO record headers). A private system-logger session is used because
# the single "NT Kernel Logger" session is usually owned by another tool. The raw capture is large, so only its first
# 16 logger buffers (1 MiB) are kept; ETL readers tolerate the missing tail.
$kernelProviders = @([guid]'151f55dc-467d-471f-83b5-5f889d46ff66') # SystemProcessProvider
Invoke-Capture 'kernel' 'kernel-classic.etl' $kernelProviders -Mode ($Sequential -bor 0x02000000) -Workload {
    1..5 | ForEach-Object { Start-Process cmd.exe -ArgumentList '/c', 'exit' -Wait }
    Start-Sleep -Seconds 1
}
$kernelFile = Join-Path $OutDir 'kernel-classic.etl'
$stream = [IO.File]::Open($kernelFile, 'Open', 'ReadWrite')
$stream.SetLength([Math]::Min($stream.Length, 1MB))
$stream.Close()
$manifestPath = Join-Path $OutDir 'fixtures.manifest.txt'
Get-ChildItem $OutDir -Filter '*.etl' | Where-Object { $_.Name -match '^(tracelogging|manifest|bulk|kernel)' } |
    Sort-Object Name | ForEach-Object {
        '{0}  {1}  {2}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Length, $_.Name
    } | Set-Content $manifestPath -Encoding ASCII
Get-Content $manifestPath
