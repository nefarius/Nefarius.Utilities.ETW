namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

internal class TraceMessageFormat
{
    public Guid MessageGuid { get; init; }

    public required string FileName { get; init; }

    public required string Opcode { get; init; }

    public int Id { get; init; }

    public required string MessageFormat { get; init; }

    public required string Level { get; init; }

    public required string Flags { get; init; }

    public required string Function { get; init; }

    public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; } = new List<FunctionParameter>();
}