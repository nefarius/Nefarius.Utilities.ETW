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
    private const ulong ProcessKeyword = 0x10;

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

    private static async Task<int> CountEventsWhileSpawningProcessesAsync(string sessionName)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        using CancellationTokenSource spawnerStop = new();
        Task spawner = Task.Run(async () =>
        {
            while (!spawnerStop.IsCancellationRequested)
            {
                using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit")
                {
                    CreateNoWindow = true, UseShellExecute = false
                });
                process?.WaitForExit();
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
                if (++received >= 3)
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