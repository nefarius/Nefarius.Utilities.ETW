using System.CommandLine;

using Nefarius.Utilities.ETW.CLI;

// ---------------------------------------------------------------------------
// Root command — composes the etwutils subcommands. Each subcommand owns its
// own arguments, options, and action handler in a dedicated *Command class.
// ---------------------------------------------------------------------------
RootCommand root = new("etwutils — ETW event decoder and offline trace parser. Stream realtime events or decode .etl files as NDJSON on stdout.")
{
    RealtimeCommand.Build(),
    InspectPdbCommand.Build(),
    ParseCommand.Build(),
    VerboseCommand.Build(),
    SessionsCommand.Build()
};

return await root.Parse(args).InvokeAsync();
