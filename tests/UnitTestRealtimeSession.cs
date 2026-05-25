using System.Security.Principal;
using System.Text.Json;

using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for <see cref="EtwRealtimeSession" /> lifecycle management.
///     These tests require administrator privileges and are skipped when the
///     test runner is not elevated.
/// </summary>
[Category("Realtime")]
[Category("RequiresAdmin")]
public class RealtimeSessionTests
{
    private const string SessionName = "Nefarius.ETW.Tests.RealtimeSession";

    [OneTimeSetUp]
    public void EnsureElevated()
    {
        if (!IsRunningAsAdmin())
        {
            Assert.Ignore("Test requires administrator privileges — skipping on non-elevated runner.");
        }

        // Clean up any orphan from a previous crashed run.
        EtwUtil.StopOrphanSession(SessionName);
    }

    [TearDown]
    public void Cleanup()
    {
        // Best-effort cleanup so a failing test does not pollute subsequent ones.
        EtwUtil.StopOrphanSession(SessionName);
    }

    // -----------------------------------------------------------------------
    // Create / Dispose lifecycle
    // -----------------------------------------------------------------------

    [Test]
    public void CreateAndDisposeSession()
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);

        Assert.That(session.SessionName, Is.EqualTo(SessionName));
    }

    [Test]
    public void DisposeIsIdempotent()
    {
        EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);

        Assert.DoesNotThrow(() =>
        {
            session.Dispose();
            session.Dispose();
        });
    }

    // -----------------------------------------------------------------------
    // Duplicate session name → ERROR_ALREADY_EXISTS
    // -----------------------------------------------------------------------

    [Test]
    public void CreatingDuplicateSessionThrows()
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);

        EtwStartTraceException ex = Assert.Throws<EtwStartTraceException>(() =>
            EtwRealtimeSession.Create(SessionName))!;

        Assert.That(ex.NativeErrorCode, Is.EqualTo(183)); // ERROR_ALREADY_EXISTS
    }

    // -----------------------------------------------------------------------
    // StopOrphanSession — stops existing, no-ops on missing
    // -----------------------------------------------------------------------

    [Test]
    public void StopOrphanSession_NoOpsWhenSessionDoesNotExist()
    {
        const string ghost = "Nefarius.ETW.Tests.NonExistent.Session";

        Assert.DoesNotThrow(() => EtwUtil.StopOrphanSession(ghost),
            "StopOrphanSession should not throw when the named session does not exist.");
    }

    [Test]
    public void StopOrphanSession_StopsRunningSession()
    {
        // Start session without disposing so we can stop it via StopOrphanSession.
        EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        // Deliberately don't dispose — simulate an orphan.

        Assert.DoesNotThrow(() => EtwUtil.StopOrphanSession(SessionName),
            "StopOrphanSession must cleanly stop an existing session.");

        // After orphan is stopped, starting a new session with the same name must succeed.
        Assert.DoesNotThrow(() =>
        {
            using EtwRealtimeSession fresh = EtwRealtimeSession.Create(SessionName);
        });

        GC.KeepAlive(session); // prevent premature finalization
    }

    // -----------------------------------------------------------------------
    // EnableProvider / DisableProvider
    // -----------------------------------------------------------------------

    [Test]
    public void EnableAndDisableProviderDoesNotThrow()
    {
        // Microsoft-Windows-Kernel-Process — well-known, always registered on Windows.
        Guid kernelProcessGuid = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");

        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);

        Assert.DoesNotThrow(() => session.EnableProvider(kernelProcessGuid, TraceEventLevel.Information));
        Assert.DoesNotThrow(() => session.DisableProvider(kernelProcessGuid));
    }

    [Test]
    public void MethodsThrowAfterDispose()
    {
        Guid someGuid = Guid.NewGuid();

        EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.EnableProvider(someGuid));
        Assert.Throws<ObjectDisposedException>(() => session.DisableProvider(someGuid));
        Assert.Throws<ObjectDisposedException>(() => session.Flush());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>
///     Argument-guard tests for <see cref="EtwRealtimeSession" /> that do not
///     require administrator privileges (no ETW session is actually started).
/// </summary>
[Category("Realtime")]
public class RealtimeSessionArgumentValidationTests
{
    [Test]
    public void CreateWithNullNameThrows() =>
        Assert.Throws<ArgumentNullException>(() => EtwRealtimeSession.Create(null!));

    [Test]
    public void CreateWithBlankNameThrows() =>
        Assert.Throws<ArgumentException>(() => EtwRealtimeSession.Create("  "));
}
