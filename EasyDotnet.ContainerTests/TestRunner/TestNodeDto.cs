namespace EasyDotnet.ContainerTests.TestRunner;

/// <summary>
/// Client-side mirror of <c>EasyDotnet.IDE.TestRunner.Models.TestNode</c>. Fields
/// match the JSON shape emitted by <c>registerTest</c> notifications.
/// <para>
/// The server's <c>NodeType</c> is serialised as <c>{ type: "Namespace" }</c>
/// (only the discriminator survives); we capture just that string here.
/// </para>
/// </summary>
public sealed record TestNodeDto(
  string Id,
  string DisplayName,
  string? ParentId,
  string? FilePath,
  int? SignatureLine,
  int? BodyStartLine,
  int? EndLine,
  NodeTypeDto Type,
  string? ProjectId,
  List<string>? AvailableActions,
  string? TargetFramework);

/// <summary>Mirror of the <c>{ type: "..." }</c> discriminator emitted for <c>NodeType</c>.</summary>
public sealed record NodeTypeDto(string Type);

/// <summary>Known <see cref="NodeTypeDto.Type"/> values.</summary>
public static class NodeTypeNames
{
  public const string Solution = "Solution";
  public const string Project = "Project";
  public const string Namespace = "Namespace";
  public const string TestClass = "TestClass";
  public const string TheoryGroup = "TheoryGroup";
  public const string TestMethod = "TestMethod";
  public const string Subcase = "Subcase";
}