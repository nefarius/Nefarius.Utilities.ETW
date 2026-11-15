using System.CommandLine;

namespace Nefarius.Utilities.ETW.Relay.CLI;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        RootCommand root = RelayCommand.Build(Console.OpenStandardInput, Console.Error);

        // Interrupts are handled by the command itself: Ctrl+C reaches both ends of a pipeline, and the relay has to
        // keep reading until its producer has written the end frame.
        InvocationConfiguration configuration = new() { ProcessTerminationTimeout = null };
        ParseResult parsed = root.Parse(args);
        int code = await parsed.InvokeAsync(configuration);
        // The parser reports its own diagnostics; usage failures are exit code 2, like the command's own checks.
        return parsed.Errors.Count > 0 ? 2 : code;
    }
}
