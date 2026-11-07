using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;

namespace EasyDotnet.IDE.Mcp.Tools;

/// <summary>
/// Turns the agent-facing selectors (ids, file + line, name, project, failed-only) into
/// testrunner node ids. Selectors narrow each other; with none, the whole solution is selected.
/// </summary>
public sealed class TestSelection(NodeRegistry registry)
{
  public IReadOnlyList<TestNode> Select(
      IReadOnlyList<string>? ids,
      string? file,
      int? line,
      string? nameContains,
      string? project,
      bool onlyFailed)
  {
    IEnumerable<TestNode> candidates;
    if (ids is { Count: > 0 })
    {
      candidates = ids.Select(id => registry.Get(id) ?? throw new ArgumentException($"Unknown test id '{id}'. Call list_tests for current ids."));
    }
    else if (file is not null)
    {
      candidates = SelectFromFile(file, line);
    }
    else if (project is not null)
    {
      candidates = MatchProjects(project).Select(id => registry.Get(id)!);
      project = null;
    }
    else
    {
      candidates = registry.GetAll().Where(n => n.Type is NodeType.Solution);
    }

    if (project is not null)
    {
      var projectIds = MatchProjects(project);
      candidates = candidates.Where(n => n.ProjectId is not null && projectIds.Contains(n.ProjectId));
    }

    if (!string.IsNullOrWhiteSpace(nameContains))
    {
      candidates = candidates
          .SelectMany(n => n.Type is NodeType.Subcase ? [n] : Tests(n))
          .Where(n => FullName(n).Contains(nameContains, StringComparison.OrdinalIgnoreCase));
    }

    if (onlyFailed)
    {
      candidates = candidates
          .SelectMany(Tests)
          .Where(n => registry.GetLastStatusKind(n.Id) is TestNodeStatusKind.Failed or TestNodeStatusKind.Faulted);
    }

    return [.. candidates.DistinctBy(n => n.Id)];
  }

  /// <summary>
  /// The node now standing for <paramref name="node"/>: itself while it exists, or the real test
  /// that replaced a not-yet-built test after discovery (same file, class and name).
  /// </summary>
  public TestNode? Current(TestNode node) =>
      registry.Get(node.Id) ?? (node.Type is NodeType.ProbableTest
          ? registry.GetAll().FirstOrDefault(n =>
              n.Type is NodeType.TestMethod or NodeType.TheoryGroup &&
              n.ParentId == node.ParentId &&
              string.Equals(n.DisplayName, node.DisplayName, StringComparison.Ordinal) &&
              string.Equals(n.FilePath, node.FilePath, StringComparison.OrdinalIgnoreCase))
          : null);

  /// <summary>Project node ids that <paramref name="nodes"/> belong to; a solution node stands for all its projects.</summary>
  public IEnumerable<string> ProjectIds(IEnumerable<TestNode> nodes) =>
      nodes
          .SelectMany(n => n.Type is NodeType.Solution
              ? registry.GetDescendants(n.Id).Where(d => d.Type is NodeType.Project).Select(d => d.Id)
              : n.ProjectId is null ? [] : [n.ProjectId])
          .Distinct();

  public string FullName(TestNode node)
  {
    var parts = new List<string>();
    for (var current = node; current is not null && current.Type is not (NodeType.Project or NodeType.Solution); current = current.ParentId is null ? null : registry.Get(current.ParentId))
    {
      parts.Add(current.DisplayName);
    }
    parts.Reverse();
    return string.Join(".", parts);
  }

  public string? ProjectName(TestNode node) =>
      node.ProjectId is null ? null : registry.Get(node.ProjectId)?.DisplayName;

  /// <summary>The method-level tests under <paramref name="node"/>, or the node itself if it is one.</summary>
  public IEnumerable<TestNode> Tests(TestNode node) =>
      IsTestLeaf(node) ? [node] : registry.GetDescendants(node.Id).Where(IsTestLeaf);

  public static bool IsTestLeaf(TestNode node) =>
      node.Type is NodeType.TestMethod or NodeType.TheoryGroup or NodeType.ProbableTest;

  private IEnumerable<TestNode> SelectFromFile(string file, int? line)
  {
    var path = Path.GetFullPath(file);
    var inFile = registry.GetNodesForFile(path).ToList();
    if (inFile.Count == 0)
      throw new ArgumentException($"No tests found in '{path}'. If they were just written, make sure the file is saved and part of a test project.");

    if (line is null)
      return inFile.Where(n => n.Type is NodeType.TestMethod or NodeType.TheoryGroup or NodeType.ProbableTest);

    // Registry lines are 0-based; callers pass the 1-based line an editor shows.
    var zeroBased = line.Value - 1;
    var enclosing = inFile
        .Where(n => n.SignatureLine is not null && n.EndLine is not null && n.SignatureLine <= zeroBased && zeroBased <= n.EndLine)
        .OrderBy(n => n.EndLine!.Value - n.SignatureLine!.Value)
        .FirstOrDefault()
        ?? throw new ArgumentException($"No test or test class encloses line {line} in '{path}'.");

    return [enclosing];
  }

  private HashSet<string> MatchProjects(string project)
  {
    var full = Path.IsPathRooted(project) || project.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(project) : null;
    var matches = registry.GetAll()
        .Where(n => n.Type is NodeType.Project)
        .Where(n => full is not null
            ? string.Equals(n.FilePath, full, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetFileNameWithoutExtension(n.FilePath), project, StringComparison.OrdinalIgnoreCase)
              || n.DisplayName.Contains(project, StringComparison.OrdinalIgnoreCase))
        .Select(n => n.Id)
        .ToHashSet();

    return matches.Count > 0
        ? matches
        : throw new ArgumentException($"No test project matches '{project}'.");
  }
}