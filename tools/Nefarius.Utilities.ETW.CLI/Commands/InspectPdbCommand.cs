using System.CommandLine;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Builds and runs the <c>inspect-pdb</c> subcommand: parse PDB files and list the WPP provider
///     GUIDs (control GUIDs) embedded via TMC: annotations.
/// </summary>
internal static class InspectPdbCommand
{
    /// <summary>
    ///     Constructs the <c>inspect-pdb</c> <see cref="Command" />, wiring up its arguments, options,
    ///     and action handler.
    /// </summary>
    internal static Command Build()
    {
        // -----------------------------------------------------------------------
        // 'inspect-pdb' subcommand – arguments and options
        // -----------------------------------------------------------------------
        Argument<string[]> inspectPathsArg = new("path")
        {
            Description =
                "One or more paths to .pdb files, directories (searched for *.pdb), " +
                "or glob patterns (e.g. C:\\Symbols\\*.pdb). TMF files are ignored as they do not " +
                "contain WPP control GUID information.",
            Arity = ArgumentArity.OneOrMore
        };

        Option<string> inspectFormatOpt = new("--format")
        {
            Description = "Output format: 'plain' (default) or 'ndjson'.",
            DefaultValueFactory = _ => "plain"
        };

        Command inspectPdb = new(
            "inspect-pdb",
            "Parse one or more PDB files and list the WPP provider GUIDs (control GUIDs) embedded in " +
            "them via TMC: annotations. No ETW session is created. Output goes to stdout; status messages " +
            "go to stderr.")
        {
            inspectPathsArg,
            inspectFormatOpt
        };

        inspectPdb.SetAction((ParseResult result) =>
        {
            string[] paths = result.GetValue(inspectPathsArg)!;
            string formatValue = result.GetValue(inspectFormatOpt)!;

            if (!formatValue.Equals("plain", StringComparison.OrdinalIgnoreCase) &&
                !formatValue.Equals("ndjson", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[!] Unknown --format value '{formatValue}'. Expected 'plain' or 'ndjson'.");
                return 1;
            }

            bool useNdjson = formatValue.Equals("ndjson", StringComparison.OrdinalIgnoreCase);

            // Collect PDB contexts only — TMF sources never carry a control GUID.
            List<PdbFileDecodingContextType> pdbContexts = [];

            foreach (string arg in paths)
            {
                if (arg.Contains('*') || arg.Contains('?'))
                {
                    if (PathGlobbing.ValidateGlobPattern(arg) is string validationError)
                    {
                        Console.Error.WriteLine(validationError);
                        continue;
                    }

                    string root = PathGlobbing.NormalizeGlobRoot(arg);
                    string pattern = Path.GetFileName(arg);
                    bool recurse = arg.Contains("**");
                    SearchOption search = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    if (!Directory.Exists(root))
                    {
                        Console.Error.WriteLine($"[!] Glob root directory not found: {root}");
                        continue;
                    }

                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(root, pattern, search))
                        {
                            SymbolResolution.TryAddPdb(pdbContexts, file);
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Console.Error.WriteLine($"[!] Access denied enumerating '{root}': {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        Console.Error.WriteLine($"[!] I/O error enumerating '{root}': {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[!] Unexpected error enumerating '{root}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (Directory.Exists(arg))
                {
                    bool any = false;
                    try
                    {
                        foreach (string pdb in Directory.EnumerateFiles(arg, "*.pdb", SearchOption.TopDirectoryOnly))
                        {
                            if (SymbolResolution.TryAddPdb(pdbContexts, pdb)) { any = true; }
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Console.Error.WriteLine($"[!] Access denied enumerating '{arg}': {ex.Message}");
                        continue;
                    }
                    catch (IOException ex)
                    {
                        Console.Error.WriteLine($"[!] I/O error enumerating '{arg}': {ex.Message}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[!] Unexpected error enumerating '{arg}': {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    if (!any)
                    {
                        Console.Error.WriteLine($"[!] No PDB files found in directory: {arg}");
                    }
                }
                else if (File.Exists(arg))
                {
                    SymbolResolution.TryAddPdb(pdbContexts, arg);
                }
                else
                {
                    Console.Error.WriteLine($"[!] Path not found: {arg}");
                }
            }

            if (pdbContexts.Count == 0)
            {
                Console.Error.WriteLine("[!] No PDB files could be loaded.");
                return 1;
            }

            // Collect all TMC records across every loaded PDB.
            List<WppTraceControl> allControls = pdbContexts
                .SelectMany(ctx => ctx.WppTraceControls)
                .DistinctBy(c => c.ControlGuid)
                .OrderBy(c => c.Name)
                .ThenBy(c => c.ControlGuid)
                .ToList();

            if (allControls.Count == 0)
            {
                Console.Error.WriteLine(
                    "[!] No WPP control GUIDs (TMC: annotations) found in the supplied PDB files. " +
                    "The PDB must have been compiled with WPP tracing enabled.");
                return 1;
            }

            if (useNdjson)
            {
                JsonSerializerOptions jsonOpts = new() { WriteIndented = false };
                foreach (WppTraceControl ctrl in allControls)
                {
                    string json = JsonSerializer.Serialize(new
                    {
                        guid = ctrl.ControlGuid.ToString("B").ToUpperInvariant(),
                        name = ctrl.Name,
                        bitFlags = ctrl.BitFlags,
                        source = ctrl.OriginalSymbolFileName ?? string.Empty
                    }, jsonOpts);
                    Console.WriteLine(json);
                }
            }
            else
            {
                foreach (WppTraceControl ctrl in allControls)
                {
                    string source = ctrl.OriginalSymbolFileName ?? "unknown";
                    Console.WriteLine(
                        $"{ctrl.ControlGuid.ToString("B").ToUpperInvariant(),-42}  " +
                        $"{ctrl.Name,-30}  " +
                        $"({source}, {ctrl.BitFlags.Count} bit flags)");

                    foreach (string flag in ctrl.BitFlags)
                    {
                        Console.WriteLine($"    {flag}");
                    }
                }
            }

            return 0;
        });

        return inspectPdb;
    }
}
