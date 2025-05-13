using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public sealed partial class Parser
{
    [GeneratedRegex(
        @"(\{{0,1}([0-9a-fA-F]){8}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){12}\}{0,1}) ([a-zA-Z0-9_\.]*)")]
    private partial Regex HeaderRegex();

    [GeneratedRegex(@"\/\/ SRC=([\w\-. ]+) MJ=")]
    private partial Regex FileNameRegex();

    [GeneratedRegex(@"^\/\/.*$")]
    private partial Regex CommentRegex();

    [GeneratedRegex(
        @"^#typev ([a-zA-Z0-9_\.]*) (\d*) ""(.*)"" \/\/ *LEVEL=([a-zA-Z0-9_]*) FLAGS=([a-zA-Z0-9_]*) FUNC=([a-zA-Z0-9_]*)")]
    private partial Regex TypeDefinitionRegex();

    [GeneratedRegex(@"^\}$")]
    private partial Regex ParamsEndRegex();

    [GeneratedRegex(@"^([ -~]*), ([a-zA-Z0-9]*) *(?:\(([^)]*)\))? \-\- (\d*) *$")]
    private partial Regex ParameterBodyRegex();

    /// <summary>
    ///     Processes a given directory of <c>.TMF</c> files and parses them.
    /// </summary>
    /// <param name="path">The directory to search in.</param>
    /// <returns>A collection of extracted <see cref="TraceMessageFormat" /> entries.</returns>
    public IReadOnlyList<TraceMessageFormat> ParseDirectory(string path)
    {
        List<TraceMessageFormat> messages = [];

        foreach (string filePath in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmf"))
        {
            using StreamReader fs = File.OpenText(filePath);

            messages.AddRange(ParseFile(fs));
        }

        return messages.Distinct().ToList().AsReadOnly();
    }

    /// <summary>
    ///     Parses a <c>.TMF</c> file and extracts all containing <see cref="TraceMessageFormat" />s.
    /// </summary>
    /// <param name="streamReader">Source file stream.</param>
    /// <returns>A collection of extracted <see cref="TraceMessageFormat" /> entries.</returns>
    public IReadOnlyList<TraceMessageFormat> ParseFile(StreamReader streamReader)
    {
        List<TraceMessageFormat> messages = [];

        Guid messageGuid = Guid.NewGuid();
        string fileName = string.Empty;
        string providerName = string.Empty;

        while (streamReader.ReadLine() is { } line)
        {
            // skip any comment lines
            if (CommentRegex().IsMatch(line))
            {
                continue;
            }

            Match headerMatch = HeaderRegex().Match(line);
            string? typeDefLine = null;

            // the first occurrence is expected to be the message GUID and module name
            if (headerMatch.Success)
            {
                messageGuid = Guid.Parse(HeaderRegex().Match(line).Groups[1].Value);
                providerName = headerMatch.Groups[7].Value;
                fileName = FileNameRegex().Match(line).Groups[1].Value;
                typeDefLine = streamReader.ReadLine();
            }
            else
            {
                // every subsequent run until EOF can be a type definition 
                typeDefLine = line;
            }

            if (string.IsNullOrEmpty(typeDefLine))
            {
                break;
            }

            Match typeDefinition = TypeDefinitionRegex().Match(typeDefLine);
            if (!typeDefinition.Success)
            {
                continue;
            }

            string opcode = typeDefinition.Groups[1].Value;
            int id = int.Parse(typeDefinition.Groups[2].Value);
            string messageFormat = typeDefinition.Groups[3].Value;
            string level = typeDefinition.Groups[4].Value;
            string flags = typeDefinition.Groups[5].Value;
            string function = typeDefinition.Groups[6].Value;

            TraceMessageFormat tmf = new()
            {
                MessageGuid = messageGuid,
                Provider = providerName,
                FileName = fileName,
                Opcode = opcode,
                Id = id,
                MessageFormat = messageFormat,
                Level = level,
                Flags = flags,
                Function = function
            };

            string? paramsBegin = streamReader.ReadLine();
            if (string.IsNullOrEmpty(paramsBegin))
            {
                continue;
            }

            List<FunctionParameter> parameters = [];

            // start of one or more parameter blocks
            while (streamReader.ReadLine() is { } paramsBody)
            {
                // end or empty, we bail
                if (ParamsEndRegex().IsMatch(paramsBody))
                {
                    break;
                }

                Match parameterMatch = ParameterBodyRegex().Match(paramsBody);

                string expression = parameterMatch.Groups[1].Value;
                ItemType type = (ItemType)Enum.Parse(typeof(ItemType), parameterMatch.Groups[2].Value);
                int varIndex = int.Parse(parameterMatch.Groups[4].Value);

                FunctionParameter parsed = new() { Expression = expression, Type = type, Index = varIndex };

                // special case
                if (type == ItemType.ItemListByte)
                {
                    string[] arrayItems = parameterMatch.Groups[3].Value.Split(',');
                    parsed.ListItems = new ReadOnlyDictionary<int, string>(
                        arrayItems.Select((value, index) => new KeyValuePair<int, string>(index, value))
                            .ToDictionary(pair => pair.Key, pair => pair.Value));
                }

                parameters.Add(parsed);
            }

            tmf.FunctionParameters = parameters.ToArray();
            messages.Add(tmf);
        }

        return messages.AsReadOnly();
    }
}