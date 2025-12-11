namespace EasyDotnet.MsBuild.ProjectModel.Syntax;

public sealed partial record PropertySyntax : MsBuildSyntaxNode
{
  public required string Name { get; init; }
  public required string Value { get; init; }
  public required string? Condition { get; init; }

  public bool HasVariableReferences => Value.Contains("$(");

  public IEnumerable<string> ReferencedVariables =>
      MsBuildPropertyRegex().Matches(Value)
          .Select(m => m.Groups[1].Value);

  [System.Text.RegularExpressions.GeneratedRegex(@"\$\((\w+)\)")]
  private static partial System.Text.RegularExpressions.Regex MsBuildPropertyRegex();

}