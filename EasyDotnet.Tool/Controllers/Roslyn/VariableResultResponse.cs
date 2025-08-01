namespace EasyDotnet.Controllers.Roslyn;

public sealed record VariableResultResponse(string Identifier, int LineStart, int LineEnd, int ColumnStart, int ColumnEnd);