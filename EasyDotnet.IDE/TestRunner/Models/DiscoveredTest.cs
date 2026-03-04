namespace EasyDotnet.IDE.TestRunner.Models;

/// <summary>
/// Normalised test discovered by either VSTest or MTP.
/// The NativeId is kept internal — never sent to Lua.
/// </summary>
public record DiscoveredTest
{
  /// <summary>VSTest GUID string or MTP Uid. Used only for dispatch to the framework.</summary>
  public required string NativeId { get; init; }

  public required string FullyQualifiedName { get; init; }

  /// <summary>
  /// Namespace segments only — does NOT include the class name.
  /// e.g. ["My", "App", "Tests"] for "My.App.Tests.MyClass.MyMethod"
  /// </summary>
  public required IReadOnlyList<string> NamespaceParts { get; init; }

  /// <summary>
  /// Simple class name only. Null for Expecto/F# tests that have no type.
  /// e.g. "MyClass"
  /// </summary>
  public required string? ClassName { get; init; }

  /// <summary>
  /// Method name, including arguments for parameterized tests.
  /// e.g. "MyMethod" or "MyMethod(1, 2)"
  /// </summary>
  public required string MethodName { get; init; }

  /// <summary>Display label shown in the tree leaf.</summary>
  public required string DisplayName { get; init; }

  /// <summary>Non-null means this is a parameterized subcase. e.g. "(a: 1, b: 2)"</summary>
  public string? Arguments { get; init; }

  public string? FilePath { get; init; }

  /// <summary>0-based (LSP standard). Both adapters normalise to this.</summary>
  public int? LineNumber { get; init; }
}