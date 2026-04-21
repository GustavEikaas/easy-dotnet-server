using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.BuildServer.MsBuildProject;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.Handlers;

/// <summary>
/// Handles singlefile/convert RPC: converts a .cs entry point file to a virtual project.
/// </summary>
public class SingleFileConvertHandler(
    RestoreHandler restoreHandler,
    ProjectPropertiesBatchHandler projectPropertiesHandler,
    ILogger<SingleFileConvertHandler> logger)
{
  [JsonRpcMethod("singlefile/convert", UseSingleObjectParameterDeserialization = true)]
  public async Task<ConvertSingleFileResponse> Convert(
        ConvertSingleFileRequest request,
        CancellationToken cancellationToken)
  {
    if (!File.Exists(request.EntryPointFilePath))
    {
      return new ConvertSingleFileResponse(
          "",
          new ProjectEvaluationResult(
              request.EntryPointFilePath,
              "Debug",
              null,
              false,
              null,
              new ProjectEvaluationError($"File not found: {request.EntryPointFilePath}", null, null)));
    }

    if (!request.EntryPointFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
    {
      return new ConvertSingleFileResponse(
          "",
          new ProjectEvaluationResult(
              request.EntryPointFilePath,
              "Debug",
              null,
              false,
              null,
              new ProjectEvaluationError("Entry point must be a .cs file", null, null)));
    }

    try
    {
      var sourceName = Path.GetFileNameWithoutExtension(request.EntryPointFilePath);
      var sourceFullPath = Path.GetFullPath(request.EntryPointFilePath);
      var virtualDir = GetVirtualProjectDirectory(sourceName, sourceFullPath);
      var csprojPath = PathJoin(virtualDir, $"{sourceName}.csproj");

      var globalProps = new Dictionary<string, string>
            {
                { "NuGetInteractive", "false" },
                { "_BuildNonexistentProjectsByDefault", "True" },
                { "RestoreUseSkipNonexistentTargets", "False" },
                { "ProvideCommandLineArgs", "True" }
            };

      var isCacheValid = CheckCacheValidity(virtualDir, sourceFullPath, globalProps, cancellationToken);

      if (!isCacheValid)
      {
        var sourceContent = await ReadFileAsync(sourceFullPath, cancellationToken);
        var directives = CSharpDirectiveParser.Parse(sourceContent);

        var buildStartFile = PathJoin(virtualDir, "build-start.cache");
        Directory.CreateDirectory(virtualDir);
        await WriteFileAsync(buildStartFile, sourceFullPath, cancellationToken);

        VirtualProjectWriter.Write(virtualDir, sourceName, sourceFullPath, directives);

        var restoreRequest = new RestoreRequest([csprojPath]);
        var restoreErrors = new List<ProjectEvaluationError>();

        await foreach (var result in restoreHandler.RestoreNugetPackages(restoreRequest, cancellationToken)
            .WithCancellation(cancellationToken))
        {
          if (!result.Success)
          {
            restoreErrors.Add(new ProjectEvaluationError(
                result.ErrorMessage ?? "Restore failed",
                null,
                null));
          }

          if (result.Output?.Diagnostics.Length > 0)
          {
            foreach (var diag in result.Output.Diagnostics)
            {
              if (diag.Severity >= BuildDiagnosticSeverity.Error)
              {
                restoreErrors.Add(new ProjectEvaluationError(
                    diag.Message ?? "Unknown restore error",
                    null,
                    null));
              }
            }
          }
        }

        if (restoreErrors.Count > 0)
        {
          return new ConvertSingleFileResponse(
              csprojPath,
              new ProjectEvaluationResult(
                  csprojPath,
                  "Debug",
                  "net10.0",
                  false,
                  null,
                  restoreErrors[0]));
        }

        var implicitFiles = ScanImplicitBuildFiles(sourceFullPath);

        var successCacheEntry = new SingleFileCacheEntry(globalProps, implicitFiles);
        var successCacheFile = PathJoin(virtualDir, "build-success.cache");
        var json = JsonSerializer.Serialize(successCacheEntry, new JsonSerializerOptions { WriteIndented = true });
        await WriteFileAsync(successCacheFile, json, cancellationToken);
      }

      var evalRequest = new GetProjectPropertiesBatchRequest(
          [csprojPath],
          "Debug");

      ProjectEvaluationResult? evalResult = null;
      await foreach (var result in projectPropertiesHandler.GetProjectPropertiesBatch(evalRequest, cancellationToken)
          .WithCancellation(cancellationToken))
      {
        evalResult = result;
        break;
      }

      if (evalResult == null)
      {
        evalResult = new ProjectEvaluationResult(
            csprojPath,
            "Debug",
            "net10.0",
            false,
            null,
            new ProjectEvaluationError("MSBuild evaluation produced no result", null, null));
      }

      return new ConvertSingleFileResponse(csprojPath, evalResult);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error converting single file: {FilePath}", request.EntryPointFilePath);
      return new ConvertSingleFileResponse(
          "",
          new ProjectEvaluationResult(
              request.EntryPointFilePath,
              "Debug",
              null,
              false,
              null,
              new ProjectEvaluationError(ex.Message, ex.StackTrace, null)));
    }
  }

  private string GetVirtualProjectDirectory(string sourceName, string sourceFullPath)
  {
    var baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Path.GetTempPath()
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    var hash = HashWithNormalizedCasing(sourceFullPath);
    var dirName = $"{sourceName}-{hash}";
    return PathJoin(baseDir, "dotnet", "runfile", dirName);
  }

  private string HashWithNormalizedCasing(string text)
  {
    var upper = text.ToUpperInvariant();
    var bytes = Encoding.UTF8.GetBytes(upper);
    byte[] hash;
#if NET6_0_OR_GREATER
    hash = System.Security.Cryptography.SHA256.HashData(bytes);
#else
    using (var sha256 = System.Security.Cryptography.SHA256.Create())
    {
      hash = sha256.ComputeHash(bytes);
    }
#endif
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
  }

  private bool CheckCacheValidity(
      string virtualDir,
      string sourceFilePath,
      Dictionary<string, string> currentGlobalProps,
      CancellationToken cancellationToken)
  {
    var buildStartFile = PathJoin(virtualDir, "build-start.cache");
    var buildSuccessFile = PathJoin(virtualDir, "build-success.cache");

    if (!File.Exists(buildSuccessFile))
    {
      logger.LogDebug("Cache invalid: build-success.cache missing");
      return false;
    }

    if (!File.Exists(buildStartFile))
    {
      logger.LogDebug("Cache invalid: build-start.cache missing");
      return false;
    }

    var startInfo = new FileInfo(buildStartFile);
    var successInfo = new FileInfo(buildSuccessFile);
    if (startInfo.LastWriteTimeUtc > successInfo.LastWriteTimeUtc)
    {
      logger.LogDebug("Cache invalid: interrupted build detected");
      return false;
    }

    var sourceInfo = new FileInfo(sourceFilePath);
    if (sourceInfo.LastWriteTimeUtc > successInfo.LastWriteTimeUtc)
    {
      logger.LogDebug("Cache invalid: source file is newer than cache");
      return false;
    }

    SingleFileCacheEntry? cacheEntry;
    try
    {
      var json = File.ReadAllText(buildSuccessFile);
      cacheEntry = JsonSerializer.Deserialize<SingleFileCacheEntry>(json);
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "Cache invalid: failed to parse cache entry");
      return false;
    }

    if (cacheEntry == null)
    {
      logger.LogDebug("Cache invalid: cache entry is null");
      return false;
    }

    if (!GlobalPropsEqual(cacheEntry.GlobalProperties, currentGlobalProps))
    {
      logger.LogDebug("Cache invalid: GlobalProperties differ");
      return false;
    }

    var currentImplicitFiles = ScanImplicitBuildFiles(sourceFilePath);
    if (!ImplicitFilesEqual(cacheEntry.ImplicitBuildFiles, currentImplicitFiles))
    {
      logger.LogDebug("Cache invalid: implicit build files differ");
      return false;
    }

    logger.LogDebug("Cache is valid");
    return true;
  }

  private Dictionary<string, DateTime> ScanImplicitBuildFiles(string sourceFilePath)
  {
    var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    var sourceDir = Path.GetDirectoryName(sourceFilePath);

    if (string.IsNullOrEmpty(sourceDir))
    {
      return result;
    }

    var implicitFileNames = new[]
    {
            "global.json",
            "nuget.config",
            "NuGet.config",
            "NuGet.Config",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "Directory.Build.rsp",
            "MSBuild.rsp"
        };

    var currentDir = sourceDir;
    var rootDir = Path.GetPathRoot(currentDir) ?? "/";

    while (currentDir != rootDir && !string.IsNullOrEmpty(currentDir))
    {
      foreach (var fileName in implicitFileNames)
      {
        var filePath = PathJoin(currentDir, fileName);
        if (File.Exists(filePath))
        {
          var info = new FileInfo(filePath);
          result[filePath] = info.LastWriteTimeUtc;
        }
      }

      currentDir = Path.GetDirectoryName(currentDir);
    }

    return result;
  }

  private bool GlobalPropsEqual(Dictionary<string, string> cached, Dictionary<string, string> current)
  {
    if (cached.Count != current.Count)
    {
      return false;
    }

    foreach (var kvp in cached)
    {
      if (!current.TryGetValue(kvp.Key, out var currentValue) || currentValue != kvp.Value)
      {
        return false;
      }
    }

    return true;
  }

  private bool ImplicitFilesEqual(Dictionary<string, DateTime> cached, Dictionary<string, DateTime> current)
  {
    foreach (var kvp in cached)
    {
      if (!File.Exists(kvp.Key))
      {
        return false;
      }

      var info = new FileInfo(kvp.Key);
      if (info.LastWriteTimeUtc != kvp.Value)
      {
        return false;
      }
    }

    foreach (var kvp in current)
    {
      if (!cached.ContainsKey(kvp.Key))
      {
        return false;
      }
    }

    return true;
  }

  private static string PathJoin(params string[] paths)
  {
#if NET5_0_OR_GREATER
    return Path.Join(paths);
#else
    return Path.Combine(paths);
#endif
  }

  private static async Task<string> ReadFileAsync(string path, CancellationToken ct)
  {
#if NET5_0_OR_GREATER
    return await File.ReadAllTextAsync(path, ct);
#else
    return File.ReadAllText(path);
#endif
  }

  private static async Task WriteFileAsync(string path, string content, CancellationToken ct)
  {
#if NET5_0_OR_GREATER
    await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct);
#else
    await Task.Run(() => File.WriteAllText(path, content, Encoding.UTF8), ct);
#endif
  }
}