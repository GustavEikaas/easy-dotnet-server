namespace EasyDotnet.Domain.Models.Test;

public record DiscoveredTest
{
  public required string Id { get; init; }
  /// <summary>
  /// The "Fully Qualified Name" used for tree construction (e.g. "Namespace.Class.Method")
  /// </summary>
  public required string FullyQualifiedName { get; init; }
  /// <summary>
  /// What the user sees in the tree leaf (e.g. "Method" or "Method(1)")
  /// </summary>
  public required string DisplayName { get; init; }
  public string? FilePath { get; init; }
  /// <summary>
  /// 0-based line number (LSP standard)
  /// </summary>
  public int? LineNumber { get; init; }

  public required IReadOnlyList<string> NamespacePath { get; init; }

  // Explicitly holds the parameters (e.g. "(1, 2)") 
  // Null means it is a standard test 
  public string? Arguments { get; init; }
}