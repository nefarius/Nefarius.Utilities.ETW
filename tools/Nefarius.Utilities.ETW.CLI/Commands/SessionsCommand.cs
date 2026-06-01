using System.CommandLine;
using System.Diagnostics;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Builds and runs the <c>sessions</c> subcommand: list or clean up running ETW sessions
///     created by etwutils.
/// </summary>
internal static class SessionsCommand
{
    /// <summary>
    ///     Constructs the <c>sessions</c> <see cref="Command" />, wiring up its arguments, options,
    ///     and action handler.
    /// </summary>
    internal static Command Build()
    {
        // -----------------------------------------------------------------------
        // 'sessions' subcommand – arguments and options
        // -----------------------------------------------------------------------
        Argument<string> sessionsActionArg = new("action")
        {
            Description =
                "Action to perform: " +
                "'list' prints all running ETW sessions and annotates NefariusEtwCli-<pid> sessions as alive or dead; " +
                "'clean' stops NefariusEtwCli-<pid> sessions whose owning process is no longer running."
        };

        Option<bool> sessionsAllOpt = new("--all")
        {
            Description =
                "For 'clean': also stop sessions whose owning process is still running. " +
                "Warning: this may interrupt another active etwutils realtime instance."
        };

        Option<string> sessionsPrefixOpt = new("--prefix")
        {
            Description = "Session name prefix to target. Default: NefariusEtwCli-.",
            DefaultValueFactory = _ => "NefariusEtwCli-"
        };

        Option<bool> sessionsDryRunOpt = new("--dry-run")
        {
            Description = "Print what would be done without stopping any sessions. Exits with code 0."
        };

        Command sessions = new(
            "sessions",
            "List or clean up running ETW sessions created by etwutils. " +
            "Useful for removing leftover NefariusEtwCli-<pid> sessions after an abrupt termination. " +
            "'clean' requires an elevated (administrator) process.")
        {
            sessionsActionArg,
            sessionsAllOpt,
            sessionsPrefixOpt,
            sessionsDryRunOpt
        };

        sessions.SetAction((ParseResult result) =>
        {
            string actionRaw = result.GetValue(sessionsActionArg)!;
            bool   doAll     = result.GetValue(sessionsAllOpt);
            string prefix    = result.GetValue(sessionsPrefixOpt)!;
            bool   dryRun    = result.GetValue(sessionsDryRunOpt);

            bool doList  = actionRaw.Equals("list",  StringComparison.OrdinalIgnoreCase);
            bool doClean = actionRaw.Equals("clean", StringComparison.OrdinalIgnoreCase);

            if (!doList && !doClean)
            {
                Console.Error.WriteLine(
                    $"[!] Unknown action '{actionRaw}'. Expected 'list' or 'clean'.");
                return 2;
            }

            IReadOnlyList<string> allSessions;
            try
            {
                allSessions = EtwUtil.EnumerateSessionNames();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[!] Failed to enumerate ETW sessions: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            if (doList)
            {
                Console.Error.WriteLine($"[*] {allSessions.Count} running ETW session(s):");
                foreach (string name in allSessions)
                {
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        TryParsePidSuffix(name, prefix, out int pid))
                    {
                        bool alive = IsProcessAlive(pid);
                        Console.WriteLine($"  {name}  [{(alive ? $"alive pid={pid}" : $"dead  pid={pid}")}]");
                    }
                    else
                    {
                        Console.WriteLine($"  {name}");
                    }
                }

                return 0;
            }

            // doClean
            IEnumerable<string> targets = allSessions
                .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            && TryParsePidSuffix(n, prefix, out int _));

            List<string> toStop = [];
            foreach (string name in targets)
            {
                TryParsePidSuffix(name, prefix, out int pid);
                bool alive = IsProcessAlive(pid);

                if (alive && !doAll)
                {
                    Console.Error.WriteLine(
                        $"[*] Skipping '{name}' — process {pid} is still running " +
                        "(pass --all to force-stop live sessions).");
                    continue;
                }

                toStop.Add(name);
            }

            if (toStop.Count == 0)
            {
                Console.Error.WriteLine("[*] No sessions to clean up.");
                return 0;
            }

            int stopped = 0;
            int failed  = 0;
            foreach (string name in toStop)
            {
                TryParsePidSuffix(name, prefix, out int pid);
                bool alive = IsProcessAlive(pid);
                string status = alive ? $"alive pid={pid}" : $"dead pid={pid}";

                if (dryRun)
                {
                    Console.Error.WriteLine($"[*] Dry run — would stop: {name}  [{status}]");
                    continue;
                }

                try
                {
                    EtwUtil.StopOrphanSession(name);
                    Console.Error.WriteLine($"[*] Stopped: {name}  [{status}]");
                    stopped++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[!] Failed to stop '{name}': {ex.GetType().Name}: {ex.Message}");
                    failed++;
                }
            }

            if (!dryRun)
            {
                Console.Error.WriteLine(
                    $"[*] Done — stopped {stopped} session(s)" + (failed > 0 ? $", {failed} failed." : "."));
            }

            return failed > 0 ? 1 : 0;
        });

        return sessions;
    }

    /// <summary>
    ///     Attempts to parse the integer PID from the suffix of <paramref name="sessionName" />
    ///     after stripping <paramref name="prefix" />.
    ///     Returns <see langword="true" /> and sets <paramref name="pid" /> on success.
    /// </summary>
    private static bool TryParsePidSuffix(string sessionName, string prefix, out int pid)
    {
        pid = -1;
        if (!sessionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = sessionName[prefix.Length..];
        return int.TryParse(suffix, out pid) && pid > 0;
    }

    /// <summary>
    ///     Returns <see langword="true" /> when a process with <paramref name="pid" /> currently exists.
    /// </summary>
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            // Process does not exist.
            return false;
        }
        catch
        {
            // Any other error (access denied, etc.) — assume alive to avoid accidental kills.
            return true;
        }
    }
}
