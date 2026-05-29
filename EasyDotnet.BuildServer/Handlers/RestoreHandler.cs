using System.Diagnostics;
using System.Runtime.CompilerServices;
using EasyDotnet.BuildServer.Contracts;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

public class RestoreHandler
{
  private static readonly SemaphoreSlim BuildLock = new(1, 1);

  [JsonRpcMethod("projects/restore", UseSingleObjectParameterDeserialization = true)]
  public async IAsyncEnumerable<RestoreResult> RestoreNugetPackages(
      RestoreRequest request,
      [EnumeratorCancellation] CancellationToken ct)
  {
    ValidateRequest(request);

    await BuildLock.WaitAsync(ct);
    try
    {
      foreach (var projectPath in request.ProjectPaths)
      {
        if (ct.IsCancellationRequested) { yield break; }

        if (!File.Exists(projectPath))
        {
          yield return new RestoreResult(
              projectPath,
              Success: false,
              ErrorMessage: "Project file not found",
              Output: new RestoreOutput(TimeSpan.Zero, []));
          continue;
        }

        yield return await RestoreProjectAsync(projectPath, request.Configuration, request.Platform, ct);
      }
    }
    finally
    {
      BuildLock.Release();
    }
  }

  private static Task<RestoreResult> RestoreProjectAsync(
      string projectPath,
      string? configuration,
      string? platform,
      CancellationToken ct) =>
      Task.Run(() =>
      {
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<BuildDiagnostic>();

        try
        {
          ct.ThrowIfCancellationRequested();

          var globalProperties = new Dictionary<string, string>();

          if (!string.IsNullOrEmpty(configuration))
          {
            globalProperties["Configuration"] = configuration!;
          }

          if (!string.IsNullOrEmpty(platform))
          {
            globalProperties["Platform"] = platform!;
          }

          var beforeRestore = RestoreArtifactSnapshot.Capture(projectPath, globalProperties);
          var logger = new DiagnosticLogger(diagnostics);
          var buildParameters = BuildServerBuildParameters.Create([logger]);

          var buildRequest = new BuildRequestData(
                projectPath,
                globalProperties: globalProperties,
                toolsVersion: null,
                targetsToBuild: ["Restore"],
                hostServices: null);

          using var buildManager = new BuildManager();
          var result = buildManager.Build(buildParameters, buildRequest);

          stopwatch.Stop();

          var success = result.OverallResult == BuildResultCode.Success;
          var noOp = false;
          string? noOpReason = null;

          if (success)
          {
            var afterRestore = RestoreArtifactSnapshot.Capture(projectPath, globalProperties);
            noOp = RestoreArtifactSnapshot.IsNoOp(beforeRestore, afterRestore, out noOpReason);
            if (noOp)
            {
              diagnostics.Add(new BuildDiagnostic(
                  File: null,
                  LineNumber: 0,
                  ColumnNumber: 0,
                  EndLineNumber: 0,
                  EndColumnNumber: 0,
                  Message: noOpReason,
                  Code: "ED-RESTORE-NOOP",
                  ProjectFile: projectPath,
                  Severity: BuildDiagnosticSeverity.Message));
            }
          }

          return new RestoreResult(
                projectPath,
                Success: success,
                ErrorMessage: success ? null : "Restore failed",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics], noOp, noOpReason));
        }
        catch (OperationCanceledException)
        {
          stopwatch.Stop();
          return new RestoreResult(
                projectPath,
                Success: false,
                ErrorMessage: "Cancelled",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics]));
        }
        catch (Exception ex)
        {
          stopwatch.Stop();
          return new RestoreResult(
                projectPath,
                Success: false,
                ErrorMessage: $"Exception during restore: {ex.Message}",
                Output: new RestoreOutput(stopwatch.Elapsed, [.. diagnostics]));
        }
      }, ct);

  private static void ValidateRequest(RestoreRequest request)
  {
    if (request.ProjectPaths is null || request.ProjectPaths.Length == 0)
      throw new ArgumentException("ProjectPaths cannot be null or empty", nameof(request));

    for (var i = 0; i < request.ProjectPaths.Length; i++)
    {
      if (string.IsNullOrWhiteSpace(request.ProjectPaths[i]))
      {
        throw new ArgumentException($"ProjectPaths[{i}] cannot be null or whitespace", nameof(request));
      }

      if (!Path.IsPathRooted(request.ProjectPaths[i]))
      {
        throw new ArgumentException($"ProjectPaths[{i}] must be an absolute path: {request.ProjectPaths[i]}", nameof(request));
      }

    }
  }

  private sealed record RestoreArtifactSnapshot(IReadOnlyDictionary<string, RestoreArtifactFingerprint> Artifacts)
  {
    public static RestoreArtifactSnapshot Capture(string projectPath, IReadOnlyDictionary<string, string> globalProperties)
    {
      var artifactPaths = ResolveArtifactPaths(projectPath, globalProperties);
      var artifacts = new Dictionary<string, RestoreArtifactFingerprint>(StringComparer.OrdinalIgnoreCase);

      foreach (var path in artifactPaths)
      {
        var file = new FileInfo(path);
        if (file.Exists)
        {
          artifacts[Path.GetFullPath(path)] = new RestoreArtifactFingerprint(file.Length, file.LastWriteTimeUtc.Ticks);
        }
      }

      return new RestoreArtifactSnapshot(artifacts);
    }

    public static bool IsNoOp(RestoreArtifactSnapshot before, RestoreArtifactSnapshot after, out string reason)
    {
      reason = "Restore artifacts were unchanged.";

      if (before.Artifacts.Count == 0)
      {
        reason = "Restore artifacts did not exist before restore.";
        return false;
      }

      if (before.Artifacts.Count != after.Artifacts.Count)
      {
        reason = "Restore artifact set changed.";
        return false;
      }

      foreach (var artifact in before.Artifacts)
      {
        var path = artifact.Key;
        var fingerprint = artifact.Value;

        if (!after.Artifacts.TryGetValue(path, out var afterFingerprint))
        {
          reason = $"Restore artifact changed: {path}";
          return false;
        }

        if (fingerprint != afterFingerprint)
        {
          reason = $"Restore artifact changed: {path}";
          return false;
        }
      }

      return true;
    }

    private static HashSet<string> ResolveArtifactPaths(string projectPath, IReadOnlyDictionary<string, string> globalProperties)
    {
      var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var projectDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory;
      var extensionsPath = Path.Combine(projectDirectory, "obj");

      var loadProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var property in globalProperties)
      {
        loadProperties[property.Key] = property.Value;
      }
      using var projectCollection = new ProjectCollection();
      Project? project = null;
      try
      {
        project = projectCollection.LoadProject(projectPath, loadProperties, toolsVersion: null);

        var evaluatedExtensionsPath = ResolvePath(projectDirectory, project.GetPropertyValue("MSBuildProjectExtensionsPath"));
        if (!string.IsNullOrWhiteSpace(evaluatedExtensionsPath))
        {
          extensionsPath = evaluatedExtensionsPath;
        }

        AddIfPresent(paths, projectDirectory, project.GetPropertyValue("ProjectAssetsFile"));
      }
      catch
      {
        // Restore itself will report evaluation failures. Artifact detection falls back to obj.
      }
      finally
      {
        if (project != null)
        {
          try { projectCollection.UnloadProject(project); } catch { }
        }
      }

      if (Directory.Exists(extensionsPath))
      {
        AddExisting(paths, Path.Combine(extensionsPath, "project.assets.json"));
        AddByPattern(paths, extensionsPath, "*.nuget.cache");
        AddByPattern(paths, extensionsPath, "*.nuget.g.props");
        AddByPattern(paths, extensionsPath, "*.nuget.g.targets");
      }
      else
      {
        paths.Add(Path.GetFullPath(Path.Combine(extensionsPath, "project.assets.json")));
      }

      return paths;
    }

    private static void AddByPattern(HashSet<string> paths, string directory, string pattern)
    {
      foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
      {
        AddExisting(paths, file);
      }
    }

    private static void AddIfPresent(HashSet<string> paths, string baseDirectory, string value)
    {
      var path = ResolvePath(baseDirectory, value);
      if (!string.IsNullOrWhiteSpace(path))
      {
        paths.Add(path);
      }
    }

    private static void AddExisting(HashSet<string> paths, string path)
    {
      paths.Add(Path.GetFullPath(path));
    }

    private static string ResolvePath(string baseDirectory, string value)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        return string.Empty;
      }

      return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
    }
  }

  private sealed record RestoreArtifactFingerprint(long Length, long LastWriteTimeUtcTicks);
}
