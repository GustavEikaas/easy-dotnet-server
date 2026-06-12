namespace EasyDotnet.Controllers.Roslyn;

public sealed record EfGeneratedSqlResponse(
  bool Success,
  string? Sql,
  string? ErrorMessage,
  string TargetProject,
  string StartupProject,
  string StartupProjectSource,
  List<string> Warnings);