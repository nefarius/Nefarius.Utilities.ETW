using sly.lexer;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public enum HeaderExpressionToken
{
    [Lexeme(@"(\{{0,1}([0-9a-fA-F]){8}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){12}\}{0,1})")]
    MessageGuid,
    [Lexeme(GenericToken.String)]
    Provider,
    [Lexeme(@"\/\/ SRC=([\w\-. ]+) MJ=")]
    FileName,
}

public enum BodyExpressionToken
{
    [Lexeme(@"^(\{{0,1}([0-9a-fA-F]){8}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){12}\}{0,1})$")]
    MessageGuid,
    [Lexeme(GenericToken.String)]
    Provider,
    [Lexeme(GenericToken.Int)]
    Opcode,
    FileName,
    LineNumber,
    Id,
    FormatString,
    Level,
    Flags,
    Function
}
