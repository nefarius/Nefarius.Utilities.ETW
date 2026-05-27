using Microsoft.Win32;

namespace Nefarius.Utilities.ETW.CLI;

internal enum ServiceKind
{
    Kernel,
    Umdf
}

/// <summary>
///     A detected driver service candidate with a resolved registry target path.
/// </summary>
internal sealed record Candidate(ServiceKind Kind, string TargetKeyPath, int? CurrentValue);

/// <summary>
///     Result of probing both kernel-mode and UMDF locations for a service name.
///     Both fields can be non-null simultaneously when a kernel and a UMDF driver share the same service name.
/// </summary>
internal sealed record DetectionResult(Candidate? Kernel, Candidate? Umdf);

/// <summary>
///     Registry helpers for reading and writing the <c>VerboseOn</c> REG_DWORD value that
///     enables WPP verbose tracing for kernel-mode and UMDF driver services.
/// </summary>
internal static class VerboseRegistry
{
    // SERVICE_KERNEL_DRIVER = 1, SERVICE_FILE_SYSTEM_DRIVER = 2
    private const int ServiceKernelDriver     = 0x1;
    private const int ServiceFilesystemDriver = 0x2;

    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";
    private const string WudfRoot     = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services";

    /// <summary>
    ///     Probes both the kernel-mode services hive and the WUDF hive for <paramref name="serviceName" />.
    ///     Returns a <see cref="DetectionResult" /> whose <see cref="DetectionResult.Kernel" /> and
    ///     <see cref="DetectionResult.Umdf" /> fields are set independently; either or both may be
    ///     <see langword="null" /> when the respective candidate is absent.
    ///     This method never decides which candidate to use — that decision belongs to the caller.
    /// </summary>
    internal static DetectionResult Detect(string serviceName)
    {
        Candidate? kernel = ProbeKernel(serviceName);
        Candidate? umdf   = ProbeUmdf(serviceName);
        return new DetectionResult(kernel, umdf);
    }

    /// <summary>
    ///     Writes <c>VerboseOn = 1</c> (REG_DWORD) at the location described by
    ///     <paramref name="candidate" />.  For kernel services the <c>Parameters</c> subkey is
    ///     created on demand when it does not already exist.
    ///     Throws <see cref="UnauthorizedAccessException" /> when the process lacks write access
    ///     (i.e. is not elevated); the caller is responsible for translating this into an error message.
    /// </summary>
    internal static void Enable(Candidate candidate)
    {
        using RegistryKey hklm    = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? target = hklm.CreateSubKey(candidate.TargetKeyPath, writable: true);

        if (target is null)
        {
            throw new InvalidOperationException(
                $"Could not open or create registry key HKLM\\{candidate.TargetKeyPath}.");
        }

        target.SetValue("VerboseOn", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    ///     Deletes the <c>VerboseOn</c> value from the location described by
    ///     <paramref name="candidate" />, if present.  When the value is already absent this is a
    ///     silent no-op.  The parent key itself is never deleted.
    ///     Throws <see cref="UnauthorizedAccessException" /> when the process lacks write access.
    /// </summary>
    internal static void Disable(Candidate candidate)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? target = hklm.OpenSubKey(candidate.TargetKeyPath, writable: true);

        // Key absent means VerboseOn cannot be set there — nothing to do.
        if (target is null) return;

        target.DeleteValue("VerboseOn", throwOnMissingValue: false);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static Candidate? ProbeKernel(string serviceName)
    {
        string serviceKeyPath = $@"{ServicesRoot}\{serviceName}";

        using RegistryKey hklm       = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? serviceKey = hklm.OpenSubKey(serviceKeyPath, writable: false);

        if (serviceKey is null) return null;

        object? typeVal = serviceKey.GetValue("Type");
        if (typeVal is not int type) return null;

        if (type != ServiceKernelDriver && type != ServiceFilesystemDriver) return null;

        // VerboseOn lives in the Parameters subkey.
        string targetPath = $@"{ServicesRoot}\{serviceName}\Parameters";
        int?   current    = ReadVerboseOn(hklm, targetPath);

        return new Candidate(ServiceKind.Kernel, targetPath, current);
    }

    private static Candidate? ProbeUmdf(string serviceName)
    {
        string wudfKeyPath = $@"{WudfRoot}\{serviceName}";

        using RegistryKey hklm    = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? wudfKey = hklm.OpenSubKey(wudfKeyPath, writable: false);

        if (wudfKey is null) return null;

        // VerboseOn is stored directly under the WUDF service key.
        int? current = ReadVerboseOn(hklm, wudfKeyPath);

        return new Candidate(ServiceKind.Umdf, wudfKeyPath, current);
    }

    private static int? ReadVerboseOn(RegistryKey hklm, string keyPath)
    {
        using RegistryKey? key = hklm.OpenSubKey(keyPath, writable: false);
        if (key is null) return null;

        object? val = key.GetValue("VerboseOn");
        return val is int i ? i : null;
    }
}
