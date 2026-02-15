using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public class RestoreNugetPackagesPostActionHandler(
  ILogger<RestoreNugetPackagesPostActionHandler> logger,
  IProgressScopeFactory progressScopeFactory,
  BuildHostManager buildHostManager) : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
  public Guid ActionId => Id;

  public async Task<bool> Handle(
        IPostAction postAction,
        IReadOnlyList<ICreationPath> primaryOutputs,
        string workingDirectory,
        CancellationToken cancellationToken)
  {
    try
    {
      var projectPaths = GetProjectPaths(postAction, primaryOutputs, workingDirectory);

      if (projectPaths.Count == 0)
      {
        logger.LogWarning("No project files found to restore");
        return true;
      }

      logger.LogInformation("Restoring NuGet packages for {Count} project(s)", projectPaths.Count);

      var request = new RestoreRequest([.. projectPaths]);
      var results = new List<RestoreResult>();

      using var restoreProgress = progressScopeFactory.Create("Restoring nuget packages", "Restoring nuget packages");
      var asyncEnumerable = await buildHostManager.RestoreNugetPackagesAsync(request, cancellationToken);

      await foreach (var result in asyncEnumerable)
      {
        results.Add(result);

        if (result.Success)
        {
          logger.LogInformation("Successfully restored: {ProjectPath}", result.ProjectPath);
        }
        else
        {
          logger.LogError("Failed to restore {ProjectPath}: {Error}", result.ProjectPath, result.ErrorMessage);
        }
      }

      return results.All(r => r.Success);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error during NuGet package restore");
      return false;
    }
  }

  private static List<string> GetProjectPaths(
       IPostAction postAction,
       IReadOnlyList<ICreationPath> primaryOutputs,
       string workingDirectory)
  {
    var projectPaths = new List<string>();

    if (postAction.Args.TryGetValue("files", out var filesArg) &&
        !string.IsNullOrWhiteSpace(filesArg))
    {
      var patterns = ParseFilesArgument(filesArg);
      var matchedFiles = MatchGlobPatterns(patterns, primaryOutputs, workingDirectory);

      if (matchedFiles.Count > 0)
      {
        return matchedFiles;
      }
    }

    foreach (var output in primaryOutputs)
    {
      if (FileTypes.IsAnyProjectFile(output.Path))
      {
        var fullPath = Path.IsPathRooted(output.Path)
            ? output.Path
            : Path.Combine(workingDirectory, output.Path);

        projectPaths.Add(fullPath);
      }
    }

    return projectPaths;
  }

  private static string[] ParseFilesArgument(string filesArg)
  {
    if (filesArg.TrimStart().StartsWith('['))
    {
      try
      {
        var array = System.Text.Json.JsonSerializer.Deserialize<string[]>(filesArg);
        return array ?? [];
      }
      catch { }
    }

    return filesArg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }

  private static List<string> MatchGlobPatterns(
      string[] patterns,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory)
  {
    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

    foreach (var pattern in patterns)
    {
      var normalizedPattern = pattern.TrimStart('.', '/').TrimStart('\\');

      if (!normalizedPattern.Contains('/') && !normalizedPattern.Contains('\\'))
      {
        matcher.AddInclude("**/" + normalizedPattern);
      }
      else
      {
        matcher.AddInclude(normalizedPattern);
      }
    }

    var matchedFiles = new List<string>();

    foreach (var output in primaryOutputs)
    {
      if (!FileTypes.IsAnyProjectFile(output.Path))
        continue;

      var normalizedPath = output.Path
          .Replace('\\', '/')
          .TrimStart('.', '/')
          .TrimStart('/');

      var matchResult = matcher.Match(normalizedPath);

      if (matchResult.HasMatches)
      {
        var fullPath = Path.IsPathRooted(output.Path)
            ? output.Path
            : Path.Combine(workingDirectory, output.Path);

        matchedFiles.Add(fullPath);
      }
    }

    return matchedFiles;
  }
}

// # Restore NuGet packages
//
// Used to restore NuGet packages after project create.
//
//  - **Action ID** : `210D431B-A78B-4D2F-B762-4ED3E3EA9025`
//  - **Specific Configuration** :
//     - `args`:
//       - `files` (string|array) (optional):
//         - `string`: A semicolon delimited list of files that should be restored. If specified, the primary outputs will be ignored for processing. If not specified, matching primary outputs are restored.
//         - `array`: An array of files that should be restored. If specified, the primary outputs will be ignored for processing. If not specified, matching primary outputs are restored.
//
//       Note: the file path specified in `files` is used as glob pattern matching relative source path that starts with `./`. If none of the patterns is matched, matching primary outputs are restored.
//
//       Given that relative source paths are:
//       - ./src/Client/Client.csproj
//       - ./src/Client/Client.Library.csproj
//       - ./src/Client/Client.Test.csproj
//
//       The following patterns can be used.
//       |Description|Glob Pattern|
//       |-|-|
//       |Exact path matching single project|`./src/Client/Client.Test.csproj`|
//       |Wildcard `*` matching multiple projects|`./src/Client/Client.*.csproj`|
//       |Globstar `**` recursively matching multiple layers of directories|`**/Client.Library.csproj;**/Client.csproj`|
//       |File name without parent path matching the project with the same name|`Client.Library.csproj`|
//
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//  - **Ignored in**:
//    - `Visual Studio` - Visual Studio restores all projects automatically, so post action will be be ignored.
//
// Note: when using `files` argument it should contain the path to the file in source template definition, and ignore all the path and filename changes that can happen when instantiating template. For more details, see [the article](Using-Primary-Outputs-for-Post-Actions.md).
//
// ### Example
//
// Restores the project mentioned in primary outputs:
//
// ```
// "primaryOutputs": [
//   {
//     "path": "MyTestProject.csproj"        
//   }
// ],
// "postActions": [{
//   "condition": "(!skipRestore)",
//   "description": "Restore NuGet packages required by this project.",
//   "manualInstructions": [{
//     "text": "Run 'dotnet restore'"
//   }],
//   "actionId": "210D431B-A78B-4D2F-B762-4ED3E3EA9025",
//   "continueOnError": true
// }]
// ```
//
// Restores the files mentioned in `files` argument. The primary outputs will be ignored.
//
// ```
// "primaryOutputs": [
//   {
//     "path": "Primary/Output/PrimaryOutput.csproj"        // will not be restored
//   }
// ],
// "postActions": [{
//   "condition": "(!skipRestore)",
//   "description": "Restore NuGet packages required by this project.",
//   "manualInstructions": [{
//     "text": "Run 'dotnet restore'"
//   }],
//   "actionId": "210D431B-A78B-4D2F-B762-4ED3E3EA9025",
//   "continueOnError": true,
//   "args": {
//     "files": ["./Client/Client.csproj", "./Server/Server.csproj"]
//   }
// }]
// ```
//
// If none of files mentioned in `files` argument is matched, the primary outputs will be restored.
//
// ```
// "primaryOutputs": [
//   {
//     "path": "Primary/Output/PrimaryOutput.csproj"        // will be restored
//   }
// ],
// "postActions": [{
//   "condition": "(!skipRestore)",
//   "description": "Restore NuGet packages required by this project.",
//   "manualInstructions": [{
//     "text": "Run 'dotnet restore'"
//   }],
//   "actionId": "210D431B-A78B-4D2F-B762-4ED3E3EA9025",
//   "continueOnError": true,
//   "args": {
//     "files": ["Client/Client.csproj"]        // This will not match any project because relative source path starts with "./"
//   }
// }]
// ```
//