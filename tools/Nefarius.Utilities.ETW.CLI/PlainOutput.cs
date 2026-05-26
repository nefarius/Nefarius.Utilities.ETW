using System.Globalization;
using System.Text.Json;

using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Static helpers for building, filtering, and rendering plain-TSV output.
///     <see cref="PlainEvent" /> and <see cref="ColumnSpec" /> are nested here so callers
///     can reference them as <c>PlainOutput.PlainEvent</c> and <c>PlainOutput.ColumnSpec</c>.
/// </summary>
internal static class PlainOutput
{
    /// <summary>
    ///     Flat DTO built from a single NDJSON event buffer for plain-TSV rendering and filtering.
    /// </summary>
    internal sealed record PlainEvent
    {
        /// <summary>ISO-8601 local timestamp with UTC offset, or "-" when absent.</summary>
        public string Timestamp { get; init; } = "-";

        /// <summary>WPP GuidName or the first segment of a TDH event name.</summary>
        public string Provider { get; init; } = "-";

        /// <summary>Provider GUID in D format, or "-".</summary>
        public string ProviderGuid { get; init; } = "-";

        /// <summary>WPP LevelName string, or "-" for non-WPP events.</summary>
        public string Level { get; init; } = "-";

        /// <summary>ETW level number 1-5 (Critical=1 … Verbose=5). 0 when not present.</summary>
        public int LevelNumber { get; init; }

        /// <summary>WPP FormattedString, or compact JSON of raw properties.</summary>
        public string Message { get; init; } = "-";

        /// <summary>Full TDH event name (Provider/Task/Opcode or "WPP").</summary>
        public string EventName { get; init; } = "-";

        /// <summary>Event Id from the event header.</summary>
        public int EventId { get; init; }

        /// <summary>Process identifier.</summary>
        public int Pid { get; init; }

        /// <summary>Thread identifier.</summary>
        public int Tid { get; init; }

        /// <summary>Processor (CPU) number.</summary>
        public int Cpu { get; init; }

        /// <summary>Activity GUID in D format, or empty string.</summary>
        public string ActivityId { get; init; } = string.Empty;

        /// <summary>Related activity GUID in D format, or empty string.</summary>
        public string RelatedActivityId { get; init; } = string.Empty;

        /// <summary>WPP FunctionName; empty for non-WPP events.</summary>
        public string Function { get; init; } = string.Empty;

        /// <summary>WPP ComponentName; empty for non-WPP events.</summary>
        public string Component { get; init; } = string.Empty;

        /// <summary>WPP SubComponentName; empty for non-WPP events.</summary>
        public string SubComponent { get; init; } = string.Empty;

        /// <summary>WPP FlagsName; empty for non-WPP events.</summary>
        public string Flags { get; init; } = string.Empty;
    }

    /// <summary>
    ///     Describes one output column: its header label and the function that renders a cell value
    ///     from a <see cref="PlainEvent" />.
    /// </summary>
    internal sealed record ColumnSpec(string Name, Func<PlainEvent, string> Render);


    // ---------------------------------------------------------------------------
    // Known column definitions — order here defines help-text listing order only.
    // ---------------------------------------------------------------------------
    private static readonly IReadOnlyDictionary<string, ColumnSpec> KnownColumns =
        new Dictionary<string, ColumnSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["Timestamp"]         = new("Timestamp",         e => e.Timestamp),
            ["Provider"]          = new("Provider",          e => e.Provider),
            ["ProviderGuid"]      = new("ProviderGuid",      e => e.ProviderGuid),
            ["Level"]             = new("Level",             e => e.Level),
            ["LevelNumber"]       = new("LevelNumber",       e => e.LevelNumber.ToString(CultureInfo.InvariantCulture)),
            ["Message"]           = new("Message",           e => e.Message),
            ["EventName"]         = new("EventName",         e => e.EventName),
            ["EventId"]           = new("EventId",           e => e.EventId.ToString(CultureInfo.InvariantCulture)),
            ["Pid"]               = new("Pid",               e => e.Pid.ToString(CultureInfo.InvariantCulture)),
            ["Tid"]               = new("Tid",               e => e.Tid.ToString(CultureInfo.InvariantCulture)),
            ["Cpu"]               = new("Cpu",               e => e.Cpu.ToString(CultureInfo.InvariantCulture)),
            ["ActivityId"]        = new("ActivityId",        e => e.ActivityId),
            ["RelatedActivityId"] = new("RelatedActivityId", e => e.RelatedActivityId),
            ["Function"]          = new("Function",          e => e.Function),
            ["Component"]         = new("Component",         e => e.Component),
            ["SubComponent"]      = new("SubComponent",      e => e.SubComponent),
            ["Flags"]             = new("Flags",             e => e.Flags),
        };

    /// <summary>Default column list — matches the pre-existing four-column plain output.</summary>
    public static readonly IReadOnlyList<ColumnSpec> DefaultColumns =
    [
        KnownColumns["Timestamp"],
        KnownColumns["Provider"],
        KnownColumns["Level"],
        KnownColumns["Message"],
    ];

    /// <summary>
    ///     Returns a comma-separated list of all known column token names for use in help text.
    /// </summary>
    public static string KnownColumnNames => string.Join(", ", KnownColumns.Keys);

    // ---------------------------------------------------------------------------
    // Column parsing
    // ---------------------------------------------------------------------------

    /// <summary>
    ///     Parses a comma-separated column list into an ordered <see cref="ColumnSpec" /> list.
    ///     Returns <see langword="null" /> and sets <paramref name="error" /> when an unknown token
    ///     is encountered.
    /// </summary>
    public static IReadOnlyList<ColumnSpec>? ParseColumns(string raw, out string? error)
    {
        List<ColumnSpec> result = [];

        foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!KnownColumns.TryGetValue(token, out ColumnSpec? spec))
            {
                error = $"[!] Unknown column token '{token}'. Known tokens: {KnownColumnNames}.";
                return null;
            }

            result.Add(spec);
        }

        if (result.Count == 0)
        {
            error = "[!] --columns value is empty. Specify at least one column token.";
            return null;
        }

        error = null;
        return result;
    }

    // ---------------------------------------------------------------------------
    // Filter compilation
    // ---------------------------------------------------------------------------

    /// <summary>
    ///     Compiles a DynamicExpresso filter predicate that receives a <see cref="PlainEvent" />
    ///     as a parameter named <c>e</c> but also exposes each property as a top-level identifier so
    ///     users can write <c>Provider != "Foo"</c> instead of <c>e.Provider != "Foo"</c>.
    /// </summary>
    /// <remarks>
    ///     The approach: parse <paramref name="expression" /> with a single typed parameter
    ///     <c>e</c> of type <see cref="PlainEvent" />, then wrap the resulting <c>Lambda&lt;Func&lt;PlainEvent,bool&gt;&gt;</c>
    ///     with a real compiled delegate.
    ///     <para>
    ///         To allow the short-hand <c>Provider != "Foo"</c> syntax we prepend property aliases
    ///         as let-variables in the Interpreter so the user expression can reference them directly.
    ///         DynamicExpresso 2.x supports this via <see cref="Interpreter.SetVariable" /> combined
    ///         with a wrapping expression: we instead compile as a full delegate taking the record
    ///         and use identifier references to the record's properties via the interpreter's
    ///         <c>Parameter</c> mechanism.
    ///     </para>
    /// </remarks>
    public static Func<PlainEvent, bool>? CompileFilter(string expression, out string? error)
    {
        try
        {
            Interpreter interpreter = new(InterpreterOptions.Default);
            interpreter.Reference(typeof(PlainEvent));

            // Expose every PlainEvent property as a top-level identifier.
            // We do this by rewriting the expression: wrap it in a lambda that takes the
            // PlainEvent, extracts each property into a let-binding, then evaluates the user
            // expression body. DynamicExpresso doesn't natively support let-bindings, so we
            // instead generate a helper expression that resolves each property and inject them
            // as additional parameters with the same name. At runtime we call the inner delegate
            // with those extracted values automatically.
            //
            // Simpler alternative actually supported by DynamicExpresso: declare the record as
            // a SINGLE typed parameter named after nothing and use a thin wrapper. The cleanest
            // approach that avoids rewriting: compile as Func<PlainEvent,bool> where the
            // parameter is named such that all properties are accessible via the property
            // shorthand through DynamicExpresso's identifier lookup.
            //
            // DynamicExpresso resolves identifiers as: parameters first, then variables, then
            // types. If we name the parameter "e" the user must write e.Provider. To get short
            // syntax we use a different tactic: parse the expression with each PlainEvent
            // property exposed as a separate Parameter<T>, then build a Func<PlainEvent,bool>
            // wrapper that unpacks the record and calls the inner delegate.

            // Build typed parameters for every exposed property.
            Parameter[] propertyParams =
            [
                new Parameter("Timestamp",         typeof(string)),
                new Parameter("Provider",          typeof(string)),
                new Parameter("ProviderGuid",      typeof(string)),
                new Parameter("Level",             typeof(string)),
                new Parameter("LevelNumber",       typeof(int)),
                new Parameter("Message",           typeof(string)),
                new Parameter("EventName",         typeof(string)),
                new Parameter("EventId",           typeof(int)),
                new Parameter("Pid",               typeof(int)),
                new Parameter("Tid",               typeof(int)),
                new Parameter("Cpu",               typeof(int)),
                new Parameter("ActivityId",        typeof(string)),
                new Parameter("RelatedActivityId", typeof(string)),
                new Parameter("Function",          typeof(string)),
                new Parameter("Component",         typeof(string)),
                new Parameter("SubComponent",      typeof(string)),
                new Parameter("Flags",             typeof(string)),
            ];

            // Parse the expression into a Lambda.
            Lambda parsed = interpreter.Parse(expression, propertyParams);

            // Cast to Func<...17 string/int args..., bool> by using LambdaExpression.Compile.
            // Instead of building a 17-arg delegate we compile to an object delegate and call
            // it via DynamicExpresso's Invoke method, unpacking the PlainEvent properties.
            Func<PlainEvent, bool> predicate = (PlainEvent evt) =>
            {
                object? result = parsed.Invoke(
                    evt.Timestamp,
                    evt.Provider,
                    evt.ProviderGuid,
                    evt.Level,
                    evt.LevelNumber,
                    evt.Message,
                    evt.EventName,
                    evt.EventId,
                    evt.Pid,
                    evt.Tid,
                    evt.Cpu,
                    evt.ActivityId,
                    evt.RelatedActivityId,
                    evt.Function,
                    evt.Component,
                    evt.SubComponent,
                    evt.Flags
                );
                return result is true;
            };

            error = null;
            return predicate;
        }
        catch (ParseException ex)
        {
            error = $"[!] Filter expression parse error: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            error = $"[!] Filter expression error: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    // ---------------------------------------------------------------------------
    // Decoding
    // ---------------------------------------------------------------------------

    /// <summary>
    ///     Parses one NDJSON event buffer into a <see cref="PlainEvent" />.
    ///     Returns <see langword="null" /> when the JSON cannot be parsed or does not contain
    ///     the expected structure.
    /// </summary>
    public static PlainEvent? Decode(ReadOnlyMemory<byte> jsonBytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonBytes);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("Event", out JsonElement evt))
                return null;

            // --- Timestamp ---
            string timestamp = "-";
            if (evt.TryGetProperty("Timestamp", out JsonElement tsEl) &&
                tsEl.TryGetInt64(out long ticks))
            {
                try
                {
                    timestamp = DateTime.FromFileTimeUtc(ticks)
                        .ToLocalTime()
                        .ToString("o", CultureInfo.InvariantCulture);
                }
                catch
                {
                    timestamp = ticks.ToString(CultureInfo.InvariantCulture);
                }
            }

            // --- Provider GUID ---
            string providerGuid = "-";
            if (evt.TryGetProperty("ProviderGuid", out JsonElement pgEl))
                providerGuid = pgEl.GetString() ?? "-";

            // --- Event name ---
            string eventName = "-";
            if (evt.TryGetProperty("Name", out JsonElement nameEl))
                eventName = nameEl.GetString() ?? "-";

            // --- Event Id ---
            int eventId = 0;
            if (evt.TryGetProperty("Id", out JsonElement idEl))
                idEl.TryGetInt32(out eventId);

            // --- Pid / Tid / Cpu ---
            int pid = 0, tid = 0, cpu = 0;
            if (evt.TryGetProperty("ProcessId", out JsonElement pidEl)) pidEl.TryGetInt32(out pid);
            if (evt.TryGetProperty("ThreadId",  out JsonElement tidEl))  tidEl.TryGetInt32(out tid);
            if (evt.TryGetProperty("ProcessorNumber", out JsonElement cpuEl)) cpuEl.TryGetInt32(out cpu);

            // --- Activity IDs ---
            string activityId = string.Empty;
            string relatedActivityId = string.Empty;
            if (evt.TryGetProperty("ActivityId",        out JsonElement aiEl))  activityId        = aiEl.GetString()  ?? string.Empty;
            if (evt.TryGetProperty("RelatedActivityId", out JsonElement raiEl)) relatedActivityId = raiEl.GetString() ?? string.Empty;

            // --- Level number (from ETW header level, buried in event) ---
            int levelNumber = 0;

            // --- WPP vs non-WPP ---
            bool isWpp = eventName == "WPP";

            string provider    = "-";
            string levelName   = "-";
            string message     = "-";
            string function    = string.Empty;
            string component   = string.Empty;
            string subComponent = string.Empty;
            string flags       = string.Empty;

            if (isWpp &&
                evt.TryGetProperty("Properties", out JsonElement propsArr) &&
                propsArr.ValueKind == JsonValueKind.Array &&
                propsArr.GetArrayLength() > 0)
            {
                JsonElement p = propsArr[0];

                if (p.TryGetProperty("GuidName",        out JsonElement gn)) provider     = gn.GetString() ?? "-";
                if (p.TryGetProperty("LevelName",       out JsonElement ln)) levelName    = ln.GetString() ?? "-";
                if (p.TryGetProperty("FormattedString", out JsonElement fs)) message      = fs.GetString() ?? "-";
                if (p.TryGetProperty("FunctionName",    out JsonElement fn)) function     = fn.GetString() ?? string.Empty;
                if (p.TryGetProperty("ComponentName",   out JsonElement cn)) component    = cn.GetString() ?? string.Empty;
                if (p.TryGetProperty("SubComponentName",out JsonElement sc)) subComponent = sc.GetString() ?? string.Empty;
                if (p.TryGetProperty("FlagsName",       out JsonElement fl)) flags        = fl.GetString() ?? string.Empty;
                if (p.TryGetProperty("CpuNumber",       out JsonElement wppCpu)) wppCpu.TryGetInt32(out cpu);

                levelNumber = DeriveLevelNumber(levelName);
            }
            else
            {
                // Non-WPP provider from Name field.
                if (!string.IsNullOrEmpty(eventName) && eventName != "-")
                {
                    int slash = eventName.IndexOf('/');
                    provider = slash > 0 ? eventName[..slash] : eventName;
                }

                if (provider == "-" && providerGuid != "-")
                    provider = providerGuid;

                // Message: compact JSON of the Properties array.
                if (evt.TryGetProperty("Properties", out JsonElement rawProps))
                    message = rawProps.GetRawText();
            }

            // Escape embedded tabs and newlines in every string field.
            return new PlainEvent
            {
                Timestamp         = EscapeCell(timestamp),
                Provider          = EscapeCell(provider),
                ProviderGuid      = EscapeCell(providerGuid),
                Level             = EscapeCell(levelName),
                LevelNumber       = levelNumber,
                Message           = EscapeCell(message),
                EventName         = EscapeCell(eventName),
                EventId           = eventId,
                Pid               = pid,
                Tid               = tid,
                Cpu               = cpu,
                ActivityId        = EscapeCell(activityId),
                RelatedActivityId = EscapeCell(relatedActivityId),
                Function          = EscapeCell(function),
                Component         = EscapeCell(component),
                SubComponent      = EscapeCell(subComponent),
                Flags             = EscapeCell(flags),
            };
        }
    }

    // ---------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------

    /// <summary>Returns a tab-separated header line for the given column list.</summary>
    public static string FormatHeader(IReadOnlyList<ColumnSpec> columns) =>
        string.Join('\t', columns.Select(c => c.Name));

    /// <summary>
    ///     Renders a single <see cref="PlainEvent" /> as a tab-separated line.
    ///     When <paramref name="useColor" /> is <see langword="true" /> and the <c>Level</c> column
    ///     is present, that cell is wrapped with ANSI SGR codes.
    /// </summary>
    public static string FormatLine(PlainEvent evt, IReadOnlyList<ColumnSpec> columns, bool useColor)
    {
        string[] cells = new string[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            string cell = columns[i].Render(evt);

            // Colorise the Level cell when requested.
            if (useColor && columns[i].Name == "Level" && cell != "-")
            {
                (string? open, string? reset) = LevelColor(cell);
                if (open is not null)
                    cell = $"{open}{cell}{reset}";
            }

            cells[i] = cell;
        }

        return string.Join('\t', cells);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>Escapes tab and newline characters in a cell value.</summary>
    private static string EscapeCell(string value) =>
        value.Replace("\t", "\\t")
             .Replace("\r\n", "\\n")
             .Replace("\n", "\\n")
             .Replace("\r", "\\n");

    /// <summary>
    ///     Maps a WPP level name string to its ANSI SGR open/close pair.
    ///     Returns <c>(null, null)</c> when no colour should be applied.
    /// </summary>
    private static (string? Open, string? Reset) LevelColor(string levelName)
    {
        const string reset = "\x1b[0m";
        ReadOnlySpan<char> s = levelName.AsSpan();

        if (s.IndexOf("CRITICAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("FATAL",    StringComparison.OrdinalIgnoreCase) >= 0)
            return ("\x1b[1;91m", reset);

        if (s.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("\x1b[31m", reset);

        if (s.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("\x1b[33m", reset);

        if (s.IndexOf("INFO", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("\x1b[36m", reset);

        if (s.IndexOf("VERBOSE", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("\x1b[90m", reset);

        return (null, null);
    }

    /// <summary>
    ///     Derives a numeric level 1-5 from a WPP level name string.
    ///     Returns 0 when unrecognised.
    /// </summary>
    private static int DeriveLevelNumber(string levelName)
    {
        ReadOnlySpan<char> s = levelName.AsSpan();
        if (s.IndexOf("CRITICAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.IndexOf("FATAL",    StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        if (s.IndexOf("ERROR",    StringComparison.OrdinalIgnoreCase) >= 0)  return 2;
        if (s.IndexOf("WARN",     StringComparison.OrdinalIgnoreCase) >= 0)  return 3;
        if (s.IndexOf("INFO",     StringComparison.OrdinalIgnoreCase) >= 0)  return 4;
        if (s.IndexOf("VERBOSE",  StringComparison.OrdinalIgnoreCase) >= 0)  return 5;
        return 0;
    }
}
