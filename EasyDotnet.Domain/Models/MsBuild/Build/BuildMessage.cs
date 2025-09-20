namespace EasyDotnet.Domain.Models.MsBuild.Build;

public sealed record BuildMessage(string Type, string FilePath, int LineNumber, int ColumnNumber, string Code, string? Message);