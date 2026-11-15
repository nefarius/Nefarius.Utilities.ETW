using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Kernel session tests: a system logger session with the system process provider and the classic NT Kernel
///     Logger. They need administrator rights and are skipped when the runner is not elevated.
/// </summary>
[SupportedOSPlatform("windows")]
[Category("Realtime")]
[Category("RequiresAdmin")]
[Platform("Win")]
public class RealtimeKernelSessionTests
{
    private const string SystemLoggerName = "Nefarius.ETW.Tests.SystemLogger";
    private const uint ProcessFlag = 0x00000001; // EVENT_TRACE_FLAG_PROCESS
    private const uint RealTimeMode = 0x00000100; // EVENT_TRACE_REAL_TIME_MODE
    private const ulong ProcessKeyword = 0x1; // SYSTEM_PROCESS_KW_GENERAL

    [OneTimeSetUp]
    public void EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Assert.Ignore("Test requires administrator privileges - skipping on non-elevated runner.");
        }

        EtwUtil.StopOrphanSession(SystemLoggerName);
    }

    [TearDown]
    public void Cleanup()
    {
        EtwUtil.StopOrphanSession(SystemLoggerName);
    }

    private static bool HasSpawnedProcessId(JsonElement root, ConcurrentBag<int> spawned)
    {
        if (!root.TryGetProperty("Event", out JsonElement evt) ||
            !evt.TryGetProperty("Properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement property in properties.EnumerateArray())
        {
            if (property.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty member in property.EnumerateObject())
            {
                if (string.Equals(member.Name, "ProcessId", StringComparison.OrdinalIgnoreCase) &&
                    member.Value.ValueKind == JsonValueKind.Number &&
                    member.Value.TryGetInt32(out int pid) && spawned.Contains(pid))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<int> CountEventsWhileSpawningProcessesAsync(string sessionName)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        using CancellationTokenSource spawnerStop = new();
        ConcurrentBag<int> spawned = [];
        Task spawner = Task.Run(async () =>
        {
            while (!spawnerStop.IsCancellationRequested)
            {
                using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
                {
                    CreateNoWindow = true, UseShellExecute = false
                });
                if (process is not null)
                {
                    spawned.Add(process.Id);
                    process.WaitForExit();
                }

                await Task.Delay(250);
            }
        });

        int received = 0;
        try
        {
            await foreach (ReadOnlyMemory<byte> eventBytes in
                           EtwUtil.EnumerateRealtimeEventsAsync(sessionName, cancellationToken: cts.Token))
            {
                using JsonDocument doc = JsonDocument.Parse(eventBytes);
                Assert.That(doc.RootElement.TryGetProperty("Event", out _), Is.True);

                // Count only process events whose subject ProcessId is a process this test spawned, not synthetic
                // or unrelated events.
                if (HasSpawnedProcessId(doc.RootElement, spawned) && ++received >= 3)
                {
                    break;
                }
            }
        }
        finally
        {
            await spawnerStop.CancelAsync();
            await spawner;
        }

        return received;
    }

    [Test]
    public async Task SystemLoggerSession_ReceivesProcessEvents()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("Enabling EtwSystemProviders GUIDs requires Windows 11 or later.");
        }

        using EtwRealtimeSession session = EtwRealtimeSession.Create(SystemLoggerName, o => o.SystemLogger = true);
        session.EnableProvider(EtwSystemProviders.Process, TraceEventLevel.Information, ProcessKeyword);

        Assert.That(await CountEventsWhileSpawningProcessesAsync(SystemLoggerName), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public async Task NtKernelLogger_CreatedWithoutConsumerCanBeAttachedToByName()
    {
        EtwRealtimeSession? session = null;
        try
        {
            session = EtwRealtimeSession.Create(EtwRealtimeSession.NtKernelLoggerName,
                o => o.EnableFlags = ProcessFlag);
        }
        catch (EtwStartTraceException ex) when (ex.NativeErrorCode == 183)
        {
            // Another tool already owns the NT Kernel Logger: attaching to it is exactly the external-session case.
            TestContext.Out.WriteLine("Attaching to the NT Kernel Logger session that already exists.");

            // A file-only session delivers nothing to a real-time consumer. It is owned by someone else, so it is
            // left untouched and the test is skipped.
            (uint LogFileMode, uint EnableFlags)? settings =
                EtwUtil.QuerySessionSettings(EtwRealtimeSession.NtKernelLoggerName);
            if (settings is null || (settings.Value.LogFileMode & RealTimeMode) == 0)
            {
                Assert.Ignore("The existing NT Kernel Logger session is not in real-time mode.");
            }

            if ((settings.Value.EnableFlags & ProcessFlag) == 0)
            {
                Assert.Ignore("The existing NT Kernel Logger session does not collect process events.");
            }
        }

        try
        {
            // The consumer attaches by name and never sees the EtwRealtimeSession object.
            Assert.That(await CountEventsWhileSpawningProcessesAsync(EtwRealtimeSession.NtKernelLoggerName),
                Is.GreaterThanOrEqualTo(3));
        }
        finally
        {
            session?.Dispose();
        }
    }
}