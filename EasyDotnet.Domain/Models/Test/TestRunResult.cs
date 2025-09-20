namespace EasyDotnet.Domain.Models.Test;

public sealed record TestRunResult
{
  public required string Id { get; init; }
  public required string Outcome { get; init; }
  public required long? Duration { get; init; }
  public required string[] StackTrace { get; init; }
  public required string[] ErrorMessage { get; init; }
  public required string[] StdOut { get; init; }
}