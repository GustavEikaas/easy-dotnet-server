using System.ComponentModel;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.TestRunner.Models;
using EasyDotnet.IDE.TestRunner.Registry;
using EasyDotnet.IDE.TestRunner.Service;
using EasyDotnet.IDE.TestRunner.Store;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EasyDotnet.IDE.Mcp.Tools;

[McpServerToolType]
public sealed class TestTools(
    TestRunnerService testRunner,
    NodeRegistry registry,
    BuildErrorStore buildErrors,
    TestSelection selection,
    McpWorkspace workspace,
    McpTestRunnerClient runnerClient)
{
  private const int MessageLineLimit = 20;
  private const int StdoutLineLimit = 20;
  private const int FrameLimit = 5;

  [McpServerTool(Name = "list_tests", Title = "List tests", ReadOnly = true, OpenWorld = false)]
  [Description("""
    Lists discovered tests (MSTest, xUnit, NUnit, TUnit, Expecto; VSTest and Microsoft.Testing.Platform)
    with their id, full name, file and 1-based line. The first call builds and discovers the solution,
    which can take a while. Use the ids, or file + line, with run_tests.
    """)]
  public Task<string> ListTestsAsync(
      [Description("Only tests in this project: a path or project name.")] string? project = null,
      [Description("Only tests in this source file.")] string? file = null,
      [Description("Only tests whose full name contains this text (case-insensitive).")] string? nameContains = null,
      [Description("Maximum tests to return.")] int maxResults = 200,
      CancellationToken ct = default) => McpErrors.SurfaceAsync(async () =>
  {
    await workspace.EnsureTestRunnerAsync(ct);

    var selected = selection.Select(ids: null, file, line: null, nameContains, project, onlyFailed: false);
    var brokenProjectIds = BrokenProjects(selected);
    var tests = selected
        .SelectMany(selection.Tests)
        .DistinctBy(n => n.Id)
        .OrderBy(n => n.FilePath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(n => n.SignatureLine)
        .ToList();

    return McpJson.Serialize(new
    {
      total = tests.Count,
      truncated = tests.Count > maxResults ? true : (bool?)null,
      buildFailed = brokenProjectIds.Count > 0 ? brokenProjectIds.Select(DescribeBuildFailure) : null,
      tests = tests.Take(maxResults).Select(n => new
      {
        id = n.Id,
        name = selection.FullName(n),
        project = selection.ProjectName(n),
        file = n.FilePath,
        line = n.SignatureLine + 1,
        lastOutcome = registry.GetLastStatusKind(n.Id)?.ToString(),
      }),
    });
  });

  [McpServerTool(Name = "run_tests", Title = "Run tests", OpenWorld = false)]
  [Description("""
    Builds the affected projects and runs the selected tests in one operation, then returns pass/fail
    counts and, for each failure, the message, the failing line in user code and the user-code stack
    frames. Selectors narrow each other; with none, every test in the solution runs. Prefer this over
    `dotnet test`: no filter syntax, one run across MTP and VSTest projects. Tests that were just written
    can be run by file and line before they have ever been built.
    """)]
  public Task<string> RunTestsAsync(
      IProgress<ProgressNotificationValue> progress,
      [Description("Test, class, namespace or project ids from list_tests.")] string[]? ids = null,
      [Description("Run tests in this source file.")] string? file = null,
      [Description("With file: run the test or test class enclosing this 1-based line.")] int? line = null,
      [Description("Only tests whose full name contains this text (case-insensitive).")] string? nameContains = null,
      [Description("Only tests in this project: a path or project name.")] string? project = null,
      [Description("Only tests that failed in their last run.")] bool onlyFailed = false,
      [Description("Maximum failures to describe in full.")] int maxFailures = 10,
      CancellationToken ct = default) => McpErrors.SurfaceAsync(async () =>
  {
    await workspace.EnsureTestRunnerAsync(ct);

    if (file is not null)
    {
      await workspace.SyncTestFileAsync(file, ct);
    }

    var roots = selection.Select(ids, file, line, nameContains, project, onlyFailed);
    if (roots.Count == 0)
    {
      return McpJson.Serialize(new { success = false, error = "No tests matched the selection." });
    }

    OperationResult result;
    using (runnerClient.ReportTo(progress))
    {
      try
      {
        result = await testRunner.RunManyAsync([.. roots.Select(n => n.Id)], source: null, ct);
      }
      catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
      {
        return McpJson.Serialize(new { success = false, error = "Another test operation is in progress; try again when it finishes." });
      }
    }

    return McpJson.Serialize(Summarize(roots, result, maxFailures));
  });

  [McpServerTool(Name = "get_test_result", Title = "Get test result", ReadOnly = true, OpenWorld = false)]
  [Description("Returns the full result of a test's last run: outcome, complete message, every stack frame and stdout.")]
  public string GetTestResult([Description("Test id from list_tests or run_tests.")] string id) => McpErrors.Surface(() =>
  {
    var node = registry.Get(id) ?? throw new ArgumentException($"Unknown test id '{id}'.");
    var details = testRunner.GetResults(id);

    return McpJson.Serialize(new
    {
      id,
      name = selection.FullName(node),
      outcome = registry.GetLastStatusKind(id)?.ToString(),
      found = details.Found,
      duration = details.DurationDisplay,
      message = details.ErrorMessage,
      failingFrame = details.FailingFrame is { } f ? Location(f.File, f.Line) : null,
      frames = details.Frames?.Select(f => new { text = f.OriginalText, location = Location(f.File, f.Line), userCode = f.IsUserCode ? true : (bool?)null }),
      stdout = details.Stdout,
    });
  });

  private object Summarize(IReadOnlyList<TestNode> roots, OperationResult result, int maxFailures)
  {
    var tests = roots
        .Select(selection.Current)
        .OfType<TestNode>()
        .SelectMany(selection.Tests)
        .DistinctBy(n => n.Id)
        .ToList();

    var brokenProjectIds = BrokenProjects(roots);

    // Tests in a project that failed to build keep the status of their previous run.
    var ran = tests.Where(n => n.ProjectId is null || !brokenProjectIds.Contains(n.ProjectId)).ToList();
    var outcomes = ran.ToLookup(n => registry.GetLastStatusKind(n.Id));
    var failed = outcomes[TestNodeStatusKind.Failed].Concat(outcomes[TestNodeStatusKind.Faulted]).ToList();
    var passed = outcomes[TestNodeStatusKind.Passed].Count();
    var skipped = outcomes[TestNodeStatusKind.Skipped].Count();
    var inconclusive = outcomes[TestNodeStatusKind.Inconclusive].Count();

    return new
    {
      success = result.Success,
      total = tests.Count,
      passed,
      failed = failed.Count,
      skipped,
      inconclusive,
      notRun = tests.Count - passed - failed.Count - skipped - inconclusive,
      buildFailed = brokenProjectIds.Count > 0 ? brokenProjectIds.Select(DescribeBuildFailure) : null,
      failures = failed.Take(maxFailures).Select(Describe),
      moreFailures = failed.Count > maxFailures ? failed.Count - maxFailures : (int?)null,
    };
  }

  /// <summary>Projects behind <paramref name="nodes"/> whose last build failed, so their tests did not run.</summary>
  private List<string> BrokenProjects(IEnumerable<TestNode> nodes) =>
      [.. selection.ProjectIds(nodes).Where(id => registry.GetLastStatusKind(id) is TestNodeStatusKind.BuildFailed)];

  private object DescribeBuildFailure(string projectId) => new
  {
    project = registry.Get(projectId)?.DisplayName,
    errors = (buildErrors.Get(projectId) ?? [])
        .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
        .DistinctBy(d => (d.File, d.LineNumber, d.Code, d.Message))
        .Take(20)
        .Select(d => new { file = d.File, line = d.LineNumber, column = d.ColumnNumber, code = d.Code, message = d.Message }),
  };

  private object Describe(TestNode node)
  {
    var details = testRunner.GetResults(node.Id);
    return new
    {
      id = node.Id,
      name = selection.FullName(node),
      message = Truncate(details.ErrorMessage, MessageLineLimit),
      location = details.FailingFrame is { } f ? Location(f.File, f.Line) : null,
      userFrames = details.Frames?
          .Where(f => f.IsUserCode)
          .Take(FrameLimit)
          .Select(f => Location(f.File, f.Line) ?? f.OriginalText),
      stdout = Tail(details.Stdout, StdoutLineLimit),
    };
  }

  private static string? Location(string? file, int? line) =>
      file is null ? null : line is null ? file : $"{file}:{line}";

  private static string[]? Truncate(string[]? lines, int limit) =>
      lines is null || lines.Length <= limit ? lines : [.. lines.Take(limit), $"... {lines.Length - limit} more line(s); see get_test_result"];

  private static string[]? Tail(string[]? lines, int limit) =>
      lines is null || lines.Length == 0 ? null : lines.Length <= limit ? lines : [$"... {lines.Length - limit} earlier line(s); see get_test_result", .. lines.TakeLast(limit)];
}