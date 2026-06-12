using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.Workspace.Services;
using EasyDotnet.RoslynLanguageServices.EfQuery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

public sealed record EfQueryLocal(string Name, string Type);

public sealed record EfQueryAnalysis(
  string QueryText,
  string ContextTypeName,
  List<EfQueryLocal> Locals,
  List<string> Usings,
  string CsprojPath,
  string TargetAssemblyPath);

public sealed record StartupProjectResolution(string CsprojPath, string? TargetPath, string Source, List<string> Warnings);

public sealed record EfGeneratedSqlResult(
  bool Success,
  string? Sql,
  string? ErrorMessage,
  string TargetProject,
  string StartupProject,
  string StartupProjectSource,
  List<string> Warnings);

public class EfQuerySqlService(
  ILogger<EfQuerySqlService> logger,
  SettingsService settingsService,
  IClientService clientService,
  WorkspaceProjectResolver projectResolver,
  WorkspaceBuildHostManager buildHostManager)
{
  private static readonly SymbolDisplayFormat FullNameFormat =
    SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

  private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

  private readonly SemaphoreSlim _workspaceLock = new(1, 1);
  private WorkspaceCacheEntry? _workspaceCache;

  private sealed class WorkspaceCacheEntry
  {
    public required string CsprojPath { get; init; }
    public required MSBuildWorkspace Workspace { get; init; }
    public required Microsoft.CodeAnalysis.Project Project { get; set; }
    public required DateTime RefreshedAt { get; set; }
  }

  /// <summary>
  /// Caches the most recently used MSBuildWorkspace: the project load (MSBuild evaluation + full
  /// compilation setup) dominates request latency, while repeat invocations on the same project
  /// only need changed documents re-read from disk. A full reload happens when the csproj, the
  /// restore assets or any referenced assembly changed.
  /// </summary>
  private async Task<Microsoft.CodeAnalysis.Project> GetOrLoadProjectAsync(string csprojPath, bool forceReload, CancellationToken cancellationToken)
  {
    await _workspaceLock.WaitAsync(cancellationToken);
    try
    {
      if (forceReload || !IsCacheValid(csprojPath))
      {
        _workspaceCache?.Workspace.Dispose();
        _workspaceCache = null;

        var loadStarted = DateTime.UtcNow;
        var workspace = MSBuildWorkspace.Create();
        var project = await workspace.OpenProjectAsync(csprojPath, cancellationToken: cancellationToken);
        _workspaceCache = new WorkspaceCacheEntry { CsprojPath = csprojPath, Workspace = workspace, Project = project, RefreshedAt = loadStarted };
        return project;
      }

      RefreshChangedDocuments(_workspaceCache!);
      return _workspaceCache!.Project;
    }
    finally
    {
      _workspaceLock.Release();
    }
  }

  private bool IsCacheValid(string csprojPath)
  {
    if (_workspaceCache is null || !PathsEqual(_workspaceCache.CsprojPath, csprojPath))
    {
      return false;
    }

    var assetsPath = Path.Combine(Path.GetDirectoryName(csprojPath)!, "obj", "project.assets.json");
    if (File.GetLastWriteTimeUtc(csprojPath) > _workspaceCache.RefreshedAt
      || (File.Exists(assetsPath) && File.GetLastWriteTimeUtc(assetsPath) > _workspaceCache.RefreshedAt))
    {
      return false;
    }

    // Referenced assemblies (project references surface as metadata dlls here) changed on disk,
    // e.g. a dependency project was rebuilt with new API surface.
    return !_workspaceCache.Project.MetadataReferences
      .OfType<PortableExecutableReference>()
      .Any(x => x.FilePath is not null && File.Exists(x.FilePath) && File.GetLastWriteTimeUtc(x.FilePath) > _workspaceCache.RefreshedAt);
  }

  private static void RefreshChangedDocuments(WorkspaceCacheEntry entry)
  {
    var refreshStarted = DateTime.UtcNow;
    var changedDocumentIds = entry.Project.Documents
      .Where(x => x.FilePath is not null && File.Exists(x.FilePath) && File.GetLastWriteTimeUtc(x.FilePath) > entry.RefreshedAt)
      .Select(x => x.Id)
      .ToList();

    var project = entry.Project;
    foreach (var documentId in changedDocumentIds)
    {
      var document = project.GetDocument(documentId)!;
      project = document.WithText(SourceText.From(File.ReadAllText(document.FilePath!))).Project;
    }

    entry.Project = project;
    entry.RefreshedAt = refreshStarted;
  }

  private static Document? FindDocument(Microsoft.CodeAnalysis.Project project, string sourceFilePath) =>
    project.Documents.FirstOrDefault(d => string.Equals(d.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase));

  public async Task<EfGeneratedSqlResult> GetGeneratedSqlAsync(string sourceFilePath, int line, int character, CancellationToken cancellationToken)
  {
    var targetCsproj = FindCsprojFromFile(sourceFilePath)
      ?? throw new FileNotFoundException($"Failed to resolve csproj for file: {sourceFilePath}");

    var startup = await ResolveStartupProjectAsync(targetCsproj, sourceFilePath, cancellationToken);
    var warnings = new List<string>(startup.Warnings);

    EfGeneratedSqlResult Fail(string errorMessage) =>
      new(false, null, errorMessage, targetCsproj, startup.CsprojPath, startup.Source, warnings);

    string[] projectsToBuild = PathsEqual(startup.CsprojPath, targetCsproj)
      ? [targetCsproj]
      : [targetCsproj, startup.CsprojPath];

    // Restore is only needed before the very first build of a project; afterwards the build and
    // the Roslyn analysis are independent and run in parallel.
    var restoreNeeded = projectsToBuild.Any(x => !File.Exists(Path.Combine(Path.GetDirectoryName(x)!, "obj", "project.assets.json")));
    var buildTask = BuildProjectsAsync(projectsToBuild, restoreNeeded, cancellationToken);

    EfQueryAnalysis? analysis;
    if (restoreNeeded)
    {
      var buildError = await buildTask;
      if (buildError is not null)
      {
        return Fail(buildError);
      }
      analysis = await AnalyzeQueryAsync(targetCsproj, sourceFilePath, line, character, cancellationToken);
    }
    else
    {
      var analysisTask = AnalyzeQueryAsync(targetCsproj, sourceFilePath, line, character, cancellationToken);
      var buildError = await buildTask;
      if (buildError is not null)
      {
        try
        {
          await analysisTask;
        }
        catch (Exception ex)
        {
          logger.LogDebug(ex, "Analysis failed while build was already failing");
        }
        return Fail(buildError);
      }
      analysis = await analysisTask;
    }

    if (analysis is null)
    {
      return Fail("No EF Core query found at cursor position");
    }

    var (startupAssemblyPath, workingDirectory) = await ResolveExecutionPathsAsync(analysis, startup, warnings, cancellationToken);

    var (sql, errorMessage) = await RunQueryRunnerAsync(analysis, startupAssemblyPath, workingDirectory, cancellationToken);
    return new EfGeneratedSqlResult(sql is not null, sql, errorMessage, targetCsproj, startup.CsprojPath, startup.Source, warnings);
  }

  /// <summary>
  /// Resolves the startup project used to activate the DbContext, mirroring how run/test commands
  /// resolve their execution target: the configured default startup project first, then
  /// <see cref="WorkspaceProjectResolver.ResolveAsync"/>, finally the current file's project.
  /// </summary>
  private async Task<StartupProjectResolution> ResolveStartupProjectAsync(string targetCsproj, string sourceFilePath, CancellationToken cancellationToken)
  {
    var defaultStartup = settingsService.GetDefaultStartupProject();
    if (defaultStartup is not null && File.Exists(defaultStartup))
    {
      return new(defaultStartup, null, "SettingsService.DefaultStartupProject", []);
    }

    if (clientService.ProjectInfo?.SolutionFile is not null)
    {
      try
      {
        var resolved = await projectResolver.ResolveAsync(sourceFilePath, useDefault: true, useLaunchProfile: false, "use as EF startup project", cancellationToken);
        if (resolved is { Kind: ExecutionTargetKind.Project, Project: { } project })
        {
          return new(project.ProjectFullPath, project.TargetPath, "WorkspaceProjectResolver.ResolveAsync", []);
        }
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Startup project resolution via WorkspaceProjectResolver failed");
      }
    }

    return new(
      targetCsproj,
      null,
      "WorkspaceProjectResolver.ResolveAsync fallback",
      ["No default startup project was configured. Used the current file project as startup project."]);
  }

  /// <summary>
  /// Determines the startup assembly handed to the runner. Returns a null startup assembly when the
  /// startup project is the target project itself or its output cannot be located, in which case the
  /// runner activates the context from the target assembly alone.
  /// </summary>
  private async Task<(string? StartupAssemblyPath, string WorkingDirectory)> ResolveExecutionPathsAsync(
    EfQueryAnalysis analysis,
    StartupProjectResolution startup,
    List<string> warnings,
    CancellationToken cancellationToken)
  {
    var targetWorkingDirectory = Path.GetDirectoryName(analysis.CsprojPath)!;
    if (PathsEqual(startup.CsprojPath, analysis.CsprojPath))
    {
      return (null, targetWorkingDirectory);
    }

    var startupTargetPath = startup.TargetPath ?? await GetTargetPathAsync(startup.CsprojPath, cancellationToken);
    if (startupTargetPath is null || !File.Exists(startupTargetPath))
    {
      warnings.Add($"Could not locate the build output of startup project '{startup.CsprojPath}'. Falling back to the target project output.");
      return (null, targetWorkingDirectory);
    }

    return (startupTargetPath, Path.GetDirectoryName(startup.CsprojPath)!);
  }

  /// <summary>
  /// Builds the given projects in one batch on the persistent build server (restore included).
  /// Returns null on success, otherwise an error message with the build diagnostics.
  /// </summary>
  private async Task<string?> BuildProjectsAsync(string[] projectPaths, bool restore, CancellationToken cancellationToken)
  {
    var results = await buildHostManager
      .BatchBuildAsync(new BatchBuildRequest(projectPaths, Configuration: null, RestoreBeforeBuild: restore), cancellationToken)
      .ToListAsync(cancellationToken);

    var failed = results
      .Where(x => x.Kind == BatchBuildResultKind.Finished && x.Success != true)
      .ToList();

    if (failed.Count == 0)
    {
      return null;
    }

    var details = failed.Select(x =>
    {
      var errors = (x.Output?.Diagnostics ?? [])
        .Where(d => d.Severity == BuildDiagnosticSeverity.Error)
        .Select(d => $"{d.File}({d.LineNumber},{d.ColumnNumber}): {d.Code} {d.Message}");
      var detail = string.Join(Environment.NewLine, errors);
      return string.IsNullOrWhiteSpace(detail) ? $"{x.ProjectPath}: {x.ErrorMessage ?? "unknown build error"}" : detail;
    });

    return $"Build failed:{Environment.NewLine}{string.Join(Environment.NewLine, details)}";
  }

  /// <summary>
  /// Resolves the project's output assembly path via the build host's cached project evaluation
  /// instead of spawning an MSBuild process. Null for multi-TFM projects.
  /// </summary>
  private async Task<string?> GetTargetPathAsync(string csprojPath, CancellationToken cancellationToken)
  {
    try
    {
      var tfm = await buildHostManager.ResolveSingleTfmAsync(csprojPath, configuration: null, platform: null, ct: cancellationToken);
      if (tfm is null)
      {
        return null;
      }
      var project = await buildHostManager.GetProjectAsync(csprojPath, tfm, ct: cancellationToken);
      return project?.TargetPath;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to resolve TargetPath for {Project}", csprojPath);
      return null;
    }
  }

  private async Task<EfQueryAnalysis?> AnalyzeQueryAsync(string csprojPath, string sourceFilePath, int line, int character, CancellationToken cancellationToken)
  {
    var project = await GetOrLoadProjectAsync(csprojPath, forceReload: false, cancellationToken);
    var document = FindDocument(project, sourceFilePath);
    if (document is null)
    {
      // The file may have been created after the workspace snapshot was taken
      project = await GetOrLoadProjectAsync(csprojPath, forceReload: true, cancellationToken);
      document = FindDocument(project, sourceFilePath)
        ?? throw new FileNotFoundException($"Document not found in project: {sourceFilePath}");
    }

    var root = await document.GetSyntaxRootAsync(cancellationToken);
    var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
    var text = await document.GetTextAsync(cancellationToken);

    if (root is null || semanticModel is null)
    {
      throw new InvalidOperationException("Unable to load syntax/semantic model");
    }

    var sourceLine = text.Lines[Math.Clamp(line, 0, text.Lines.Count - 1)];
    var position = Math.Min(sourceLine.Start + Math.Max(character, 0), sourceLine.End);

    var detection = EfQueryDetector.FindQuery(root, semanticModel, position, cancellationToken);
    if (detection is null)
    {
      return null;
    }

    var (queryExpression, contextNodes) = detection;
    var contextType = semanticModel.GetTypeInfo(contextNodes[0], cancellationToken).Type!;
    var queryText = RewriteQueryText(queryExpression, contextNodes, semanticModel, cancellationToken);
    var locals = CollectCapturedLocals(semanticModel, queryExpression, contextNodes, cancellationToken);
    var usings = CollectUsings(semanticModel, root, position, cancellationToken);

    var targetAssemblyPath = project.OutputFilePath
      ?? throw new InvalidOperationException("Unable to determine project output assembly path");

    return new EfQueryAnalysis(
      queryText,
      contextType.ToDisplayString(FullNameFormat),
      locals,
      usings,
      csprojPath,
      targetAssemblyPath);
  }

  /// <summary>
  /// Collects the using directives of the source file plus the enclosing namespace chain at the
  /// cursor, so short type names in the query text resolve the same way they do in the file.
  /// </summary>
  private static List<string> CollectUsings(SemanticModel semanticModel, SyntaxNode root, int position, CancellationToken cancellationToken)
  {
    var usings = root.DescendantNodes()
      .OfType<UsingDirectiveSyntax>()
      .Select(x => x.ToString().Replace("global using", "using"))
      .ToList();

    for (var ns = semanticModel.GetEnclosingSymbol(position, cancellationToken)?.ContainingNamespace;
         ns is { IsGlobalNamespace: false };
         ns = ns.ContainingNamespace)
    {
      usings.Add($"using {ns.ToDisplayString()};");
    }

    return [.. usings.Distinct()];
  }

  /// <summary>
  /// Rewrites the query text so it compiles standalone in the runner script: DbContext references
  /// become the __ctx placeholder, and every type name is replaced with the fully qualified form of
  /// the symbol it binds to at the original location. The latter makes nested types, using-aliases
  /// and same-named types in sibling namespaces resolve identically to the source file.
  /// </summary>
  private static string RewriteQueryText(
    ExpressionSyntax queryExpression,
    List<ExpressionSyntax> contextNodes,
    SemanticModel semanticModel,
    CancellationToken cancellationToken)
  {
    var edits = contextNodes
      .Select(x => (x.SpanStart, x.Span.Length, Replacement: "__ctx"))
      .ToList();

    var typeNames = queryExpression
      .DescendantNodes()
      .OfType<SimpleNameSyntax>()
      .Where(x => x is not IdentifierNameSyntax { IsVar: true })
      .Where(x => !contextNodes.Any(c => c.Span.Contains(x.Span)))
      .Where(x => !IsRightSideOfQualifier(x, semanticModel, cancellationToken))
      .Where(x => semanticModel.GetSymbolInfo(x, cancellationToken).Symbol is INamedTypeSymbol);

    foreach (var name in typeNames)
    {
      var type = (INamedTypeSymbol)semanticModel.GetSymbolInfo(name, cancellationToken).Symbol!;
      edits.Add((name.Identifier.SpanStart, name.Identifier.Span.Length, QualifyTypeName(type)));
    }

    var builder = new StringBuilder(queryExpression.ToString());
    foreach (var (start, length, replacement) in edits.OrderByDescending(x => x.SpanStart))
    {
      builder.Remove(start - queryExpression.SpanStart, length);
      builder.Insert(start - queryExpression.SpanStart, replacement);
    }
    return builder.ToString();
  }

  /// <summary>
  /// True when the name is already qualified by a namespace or type on its left, in which case
  /// qualifying it again would corrupt the expression (the leftmost name gets qualified instead).
  /// </summary>
  private static bool IsRightSideOfQualifier(SimpleNameSyntax name, SemanticModel semanticModel, CancellationToken cancellationToken) =>
    name.Parent switch
    {
      QualifiedNameSyntax qualified => qualified.Right == name,
      MemberAccessExpressionSyntax member => member.Name == name
        && semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol is INamespaceOrTypeSymbol,
      _ => false
    };

  /// <summary>
  /// Fully qualified name of the type without generic type arguments (those are separate syntax
  /// nodes and get rewritten individually), e.g. global::My.Ns.OuterClass.JobTaskTypeDto.
  /// </summary>
  private static string QualifyTypeName(INamedTypeSymbol type) =>
    type.ContainingType is { } outer
      ? $"{QualifyTypeName(outer)}.{type.Name}"
      : type.ContainingNamespace.IsGlobalNamespace
        ? $"global::{type.Name}"
        : $"{type.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{type.Name}";

  private static List<EfQueryLocal> CollectCapturedLocals(
    SemanticModel semanticModel,
    ExpressionSyntax queryExpression,
    List<ExpressionSyntax> contextNodes,
    CancellationToken cancellationToken) =>
    [.. queryExpression
      .DescendantNodes()
      .OfType<IdentifierNameSyntax>()
      .Where(x => !contextNodes.Any(c => c.Span.Contains(x.Span)))
      .Select(x => semanticModel.GetSymbolInfo(x, cancellationToken).Symbol)
      .Where(x => x is ILocalSymbol or IParameterSymbol)
      .Where(x => x!.DeclaringSyntaxReferences.All(r => !queryExpression.Span.Contains(r.Span)))
      .Distinct(SymbolEqualityComparer.Default)
      .Select(x => new EfQueryLocal(
        x!.Name,
        (x switch
        {
          ILocalSymbol local => local.Type,
          IParameterSymbol parameter => parameter.Type,
          _ => throw new InvalidOperationException()
        }).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))];

  private async Task<(string? Sql, string? ErrorMessage)> RunQueryRunnerAsync(
    EfQueryAnalysis analysis,
    string? startupAssemblyPath,
    string workingDirectory,
    CancellationToken cancellationToken)
  {
    var runnerPath = EfQueryRunnerLocator.GetPath();
    var queryB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(analysis.QueryText));
    var localsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(analysis.Locals)));
    var usingsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(analysis.Usings)));

    var hostAssemblyPath = startupAssemblyPath ?? analysis.TargetAssemblyPath;
    var runtimeConfigPath = Path.Combine(
      Path.GetDirectoryName(hostAssemblyPath)!,
      $"{Path.GetFileNameWithoutExtension(hostAssemblyPath)}.runtimeconfig.json");

    var runtimeConfigArg = File.Exists(runtimeConfigPath) ? $"--runtimeconfig \"{runtimeConfigPath}\" " : "";
    var startupAssemblyArg = startupAssemblyPath is not null ? $"--startup-assembly \"{startupAssemblyPath}\" " : "";
    var arguments = $"exec {runtimeConfigArg}\"{runnerPath}\" " +
      $"--target-assembly \"{analysis.TargetAssemblyPath}\" " +
      startupAssemblyArg +
      $"--context-type \"{analysis.ContextTypeName}\" " +
      $"--query-b64 {queryB64} " +
      $"--locals-b64 {localsB64} " +
      $"--usings-b64 {usingsB64}";

    logger.LogDebug("Running EF query runner: dotnet {Arguments}", arguments);

    var (_, output) = await RunProcessAsync("dotnet", arguments, workingDirectory, cancellationToken);

    var resultLine = output
      .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .LastOrDefault(x => x.StartsWith('{'));

    if (resultLine is null)
    {
      return (null, $"EF query runner produced no result:{Environment.NewLine}{output}");
    }

    var result = JsonSerializer.Deserialize<RunnerResult>(resultLine, WebJsonOptions);
    return result?.Sql is not null
      ? (result.Sql, null)
      : (null, result?.Error ?? "Unknown error from EF query runner");
  }

  private sealed record RunnerResult(string? Sql, string? Error);

  private static async Task<(bool Success, string Output)> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };

    using var process = new Process { StartInfo = startInfo };
    var output = new StringBuilder();

    process.OutputDataReceived += (_, e) =>
    {
      if (e.Data != null) output.AppendLine(e.Data);
    };
    process.ErrorDataReceived += (_, e) =>
    {
      if (e.Data != null) output.AppendLine(e.Data);
    };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    await process.WaitForExitAsync(cancellationToken);

    return (process.ExitCode == 0, output.ToString());
  }

  private static bool PathsEqual(string a, string b) =>
    string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

  private static string? FindCsprojFromFile(string filePath)
  {
    var dir = Path.GetDirectoryName(filePath);
    while (dir is not null)
    {
      var csproj = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
      if (csproj is not null) return csproj;
      dir = Directory.GetParent(dir)?.FullName;
    }
    return null;
  }
}