namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

internal class TraceMessageFormat
{
    public Guid MessageGuid { get; set; }

    public required string  FileName { get; set; }

    public required string Opcode { get; set; }

    public int Id { get; set; }

    public required string MessageFormat { get; set; }

    public required string Level { get; set; }

    public required string Flags { get; set; }

    public required string  Function { get; set; }

    public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; } = new List<FunctionParameter>();
}