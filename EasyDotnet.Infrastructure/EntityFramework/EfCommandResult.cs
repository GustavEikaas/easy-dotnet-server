namespace EasyDotnet.Infrastructure.EntityFramework;

public record EfCommandResult(
  int ExitCode,
  bool Success,
  string? JsonData,
  string? ErrorMessage,
  string[] InfoMessages,
  string[] ErrorMessages,
  string StandardOutput,
  string StandardError);