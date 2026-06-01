using System.CommandLine;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Builds and runs the <c>verbose</c> subcommand: enable or disable WPP verbose tracing for a
///     kernel-mode or UMDF driver service by writing the <c>VerboseOn</c> REG_DWORD value.
/// </summary>
internal static class VerboseCommand
{
    /// <summary>
    ///     Constructs the <c>verbose</c> <see cref="Command" />, wiring up its arguments, options,
    ///     and action handler.
    /// </summary>
    internal static Command Build()
    {
        // -----------------------------------------------------------------------
        // 'verbose' subcommand – arguments and options
        // -----------------------------------------------------------------------
        Argument<string> verboseServiceArg = new("service-name")
        {
            Description = "Name of the driver service to target (e.g. BthPS3)."
        };

        Argument<string> verboseActionArg = new("action")
        {
            Description =
                "Action to perform: " +
                "'enable' writes VerboseOn = 1 (Parameters subkey for kernel-mode; service key directly for UMDF); " +
                "'disable' deletes the VerboseOn value (no-op when already absent); " +
                "'status' prints the current VerboseOn state without making changes."
        };

        Option<string?> verboseTypeOpt = new("--type")
        {
            Description =
                "Target a specific driver kind: 'kernel' or 'umdf'. " +
                "When omitted the command prefers a kernel-mode service and falls back to UMDF with a warning."
        };

        Option<bool> verboseDryRunOpt = new("--dry-run")
        {
            Description = "Print what would be done without touching the registry. Exits with code 0."
        };

        Command verbose = new(
            "verbose",
            "Enable or disable WPP verbose tracing for a kernel-mode or UMDF driver service by " +
            "writing the VerboseOn REG_DWORD to the driver's registry key. " +
            "Requires an elevated (admin) process for 'enable' and 'disable'.")
        {
            verboseServiceArg,
            verboseActionArg,
            verboseTypeOpt,
            verboseDryRunOpt
        };

        verbose.SetAction((ParseResult result) =>
        {
            string  serviceName = result.GetValue(verboseServiceArg)!;
            string  actionRaw   = result.GetValue(verboseActionArg)!;
            string? typeRaw     = result.GetValue(verboseTypeOpt);
            bool    dryRun      = result.GetValue(verboseDryRunOpt);

            // --- Validate service name ---
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                Console.Error.WriteLine("[!] service-name must not be empty or whitespace.");
                return 2;
            }

            // Reject path separators and common invalid registry key characters to prevent
            // a caller from composing registry paths outside the expected service subtree.
            if (serviceName.IndexOfAny(['\\', '/', '\0']) >= 0)
            {
                Console.Error.WriteLine(
                    "[!] service-name must not contain path separators ('\\', '/') or null characters.");
                return 2;
            }

            // --- Validate action ---
            bool doEnable = actionRaw.Equals("enable", StringComparison.OrdinalIgnoreCase);
            bool doDisable = actionRaw.Equals("disable", StringComparison.OrdinalIgnoreCase);
            bool doStatus  = actionRaw.Equals("status", StringComparison.OrdinalIgnoreCase);

            if (!doEnable && !doDisable && !doStatus)
            {
                Console.Error.WriteLine(
                    $"[!] Unknown action '{actionRaw}'. Expected 'enable', 'disable', or 'status'.");
                return 2;
            }

            // --- Validate --type ---
            ServiceKind? requestedKind = null;
            if (typeRaw is not null)
            {
                if (typeRaw.Equals("kernel", StringComparison.OrdinalIgnoreCase))
                {
                    requestedKind = ServiceKind.Kernel;
                }
                else if (typeRaw.Equals("umdf", StringComparison.OrdinalIgnoreCase))
                {
                    requestedKind = ServiceKind.Umdf;
                }
                else
                {
                    Console.Error.WriteLine(
                        $"[!] Unknown --type value '{typeRaw}'. Expected 'kernel' or 'umdf'.");
                    return 2;
                }
            }

            // --- Detect kernel and UMDF candidates ---
            DetectionResult detection;
            try
            {
                detection = VerboseRegistry.Detect(serviceName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[!] Registry probe failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            // --- Status: always show both probes and exit ---
            if (doStatus)
            {
                string kernelStatus = detection.Kernel is null
                    ? "absent"
                    : $"present (VerboseOn={(detection.Kernel.CurrentValue.HasValue ? detection.Kernel.CurrentValue.Value.ToString() : "<not set>")})";

                string umdfStatus = detection.Umdf is null
                    ? "absent"
                    : $"present (VerboseOn={(detection.Umdf.CurrentValue.HasValue ? detection.Umdf.CurrentValue.Value.ToString() : "<not set>")})";

                Console.WriteLine($"status: {serviceName}  kernel={kernelStatus}  umdf={umdfStatus}");
                return 0;
            }

            // --- Resolve the target candidate for enable/disable ---
            Candidate? target;

            if (requestedKind is not null)
            {
                target = requestedKind == ServiceKind.Kernel ? detection.Kernel : detection.Umdf;

                if (target is null)
                {
                    Console.Error.WriteLine(
                        $"[!] Service '{serviceName}' was not found as a {requestedKind.Value.ToString().ToLowerInvariant()} driver service.");
                    return 2;
                }
            }
            else
            {
                if (detection.Kernel is not null)
                {
                    target = detection.Kernel;
                }
                else if (detection.Umdf is not null)
                {
                    Console.Error.WriteLine(
                        $"[*] No kernel-mode service named '{serviceName}' found; falling back to UMDF service. " +
                        "Use --type kernel|umdf to silence this warning.");
                    target = detection.Umdf;
                }
                else
                {
                    Console.Error.WriteLine(
                        $"[!] Service '{serviceName}' was not found as a kernel driver or UMDF driver service.");
                    return 2;
                }
            }

            string kindLabel   = target.Kind == ServiceKind.Kernel ? "kernel" : "umdf";
            string actionLabel = doEnable ? "enable" : "disable";
            string targetPath  = $@"HKLM\{target.TargetKeyPath}\VerboseOn";

            if (dryRun)
            {
                Console.Error.WriteLine($"[*] Dry run — would {actionLabel}: {targetPath} ({kindLabel})");
                Console.WriteLine($"would {actionLabel} VerboseOn at {targetPath} ({kindLabel})");
                return 0;
            }

            try
            {
                if (doEnable)
                {
                    VerboseRegistry.Enable(target);
                    Console.Error.WriteLine($"[*] Enabled VerboseOn at {targetPath} ({kindLabel})");
                    Console.WriteLine($"enabled VerboseOn at {targetPath} ({kindLabel})");
                }
                else
                {
                    VerboseRegistry.Disable(target);
                    Console.Error.WriteLine($"[*] Disabled VerboseOn at {targetPath} ({kindLabel})");
                    Console.WriteLine($"disabled VerboseOn at {targetPath} ({kindLabel})");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"[!] Access denied writing to {targetPath}. " +
                    "Run etwutils as Administrator.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[!] Registry write failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            return 0;
        });

        return verbose;
    }
}
