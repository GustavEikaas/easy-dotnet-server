using System.Diagnostics;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public class AddReferenceToProjectFilePostActionHandler : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("B17581D1-C5C9-4489-8F0A-004BE667B814");
  public Guid ActionId => Id;

  public async Task<bool> Handle(
       IPostAction postAction,
       IReadOnlyList<ICreationPath> primaryOutputs,
       string workingDirectory,
       CancellationToken cancellationToken)
  {
    try
    {
      var config = BuildConfig(postAction);
      var targetFiles = GetTargetProjectFiles(config, workingDirectory);

      if (targetFiles.Count == 0)
      {
        return false;
      }

      foreach (var targetFile in targetFiles)
      {
        var success = config.ReferenceType.ToLower() switch
        {
          "package" => await AddPackageReferenceAsync(targetFile, config, cancellationToken),
          "project" => await AddProjectReferenceAsync(targetFile, config, workingDirectory, cancellationToken),
          _ => false
        };

        if (!success)
        {
          return false;
        }
      }

      return true;
    }
    catch
    {
      return false;
    }
  }

  private static async Task<bool> AddPackageReferenceAsync(
      string projectFile,
      ReferenceConfig config,
      CancellationToken cancellationToken)
  {
    var args = $"add \"{projectFile}\" package {config.Reference}";

    if (!string.IsNullOrWhiteSpace(config.Version))
    {
      args += $" --version {config.Version}";
    }

    return await RunDotnetCommandAsync(args, Path.GetDirectoryName(projectFile)!, cancellationToken);
  }

  private static async Task<bool> AddProjectReferenceAsync(
    string projectFile,
    ReferenceConfig config,
    string workingDirectory,
    CancellationToken cancellationToken)
  {
    var referencePath = config.Reference;

    if (!Path.IsPathRooted(referencePath))
    {
      referencePath = Path.Combine(workingDirectory, referencePath);
    }

    var args = $"add \"{projectFile}\" reference \"{referencePath}\"";

    return await RunDotnetCommandAsync(args, workingDirectory, cancellationToken);
  }

  private static async Task<bool> RunDotnetCommandAsync(
      string arguments,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    var psi = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = arguments,
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = new Process { StartInfo = psi };
    process.Start();

    await process.WaitForExitAsync(cancellationToken);

    return process.ExitCode == 0;
  }

  private static List<string> GetTargetProjectFiles(
      ReferenceConfig config,
      string workingDirectory)
  {
    if (config.TargetFiles.Count > 0)
    {
      return [.. config.TargetFiles
          .Select(f => Path.IsPathRooted(f) ? f : Path.Combine(workingDirectory, f))
          .Where(File.Exists)];
    }

    return FindProjectFileInDirectoryOrParent(
        workingDirectory,
        config.ProjectFileExtensions);
  }

  private static List<string> FindProjectFileInDirectoryOrParent(
      string startDirectory,
      string[] extensions)
  {
    var directory = startDirectory;
    var searchPatterns = extensions.Length > 0
        ? extensions
        : ["*.*proj"];

    while (directory != null)
    {
      foreach (var pattern in searchPatterns)
      {
        var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        if (files.Length > 0)
        {
          return [files[0]];
        }
      }

      directory = Directory.GetParent(directory)?.FullName;
    }

    return [];
  }

  private static ReferenceConfig BuildConfig(IPostAction postAction)
  {
    var args = postAction.Args;

    if (!args.TryGetValue("referenceType", out var referenceType) ||
        string.IsNullOrWhiteSpace(referenceType))
    {
      throw new InvalidOperationException("Missing required argument: referenceType");
    }

    if (!args.TryGetValue("reference", out var reference) ||
        string.IsNullOrWhiteSpace(reference))
    {
      throw new InvalidOperationException("Missing required argument: reference");
    }

    args.TryGetValue("version", out var version);

    var targetFiles = new List<string>();
    if (args.TryGetValue("targetFiles", out var targetFilesArg) &&
        !string.IsNullOrWhiteSpace(targetFilesArg))
    {
      if (targetFilesArg.TrimStart().StartsWith('['))
      {
        try
        {
          var array = System.Text.Json.JsonSerializer.Deserialize<string[]>(targetFilesArg);
          if (array != null)
            targetFiles.AddRange(array);
        }
        catch { }
      }
      else
      {
        targetFiles.AddRange(
            targetFilesArg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
      }
    }

    var extensions = Array.Empty<string>();
    if (args.TryGetValue("projectFileExtensions", out var extensionsArg) &&
        !string.IsNullOrWhiteSpace(extensionsArg))
    {
      extensions = [.. extensionsArg
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(e =>
        {
          var ext = e.StartsWith('.') ? e : "." + e;
          ext = ext.TrimStart('*');
          return "*" + ext;
        })];
    }

    return new ReferenceConfig(
        targetFiles,
        referenceType,
        reference,
        version,
        extensions);
  }

  private sealed record ReferenceConfig(
      List<string> TargetFiles,
      string ReferenceType,
      string Reference,
      string? Version,
      string[] ProjectFileExtensions);
}


// # Add a reference to a project file
//  - **Action ID** : `B17581D1-C5C9-4489-8F0A-004BE667B814`
//  - **Specific Configuration** :
//    - `args`
//      - `targetFiles` (string|array) (optional):
//         - `string`: A semicolon delimited list of files that should be processed.  If not specified, the project file in output directory or its closest parent directory will be used.
//         - `array`: An array of files that should be processed. If not specified, the project file in output directory or its closest parent directory will be used.
//      - `referenceType` (string): Either "package" or "project".
//      - `reference` (string): The package ID or relative path of the project to add the reference to.
//      - `projectFileExtensions` (string) (optional): A semicolon delimited list of literal file extensions to use when searching for the project to add the reference to. If not specified, `*.*proj` mask is used when searching.
//      - `version` (string) (optional) (package referenceType only): The version of the package to install.
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//
// Note: when using `targetFiles` argument it should contain the path to the file in source template definition, and ignore all the path and filename changes that can happen when instantiating template. For more details, see [the article](Using-Primary-Outputs-for-Post-Actions.md).
//
// ### Example
//
// Adds a reference `Microsoft.NET.Sdk.Functions` to the project file.
//
// ```
// "postActions": [{
//   "Description": "Adding Reference to Microsoft.NET.Sdk.Functions NuGet package",
//   "ActionId": "B17581D1-C5C9-4489-8F0A-004BE667B814",
//   "ContinueOnError": "false",
//   "ManualInstructions": [{
//     "Text": "Manually add the reference to Microsoft.NET.Sdk.Functions to your project file"
//   }],
//   "args": {
//     "referenceType": "package",
//     "reference": "Microsoft.NET.Sdk.Functions",
//     "version": "1.0.0",
//     "projectFileExtensions": ".csproj"
//   }
// }]
// ```
//
// Includes a reference to `SomeDependency` into `MyProjectFile`. The referenced project file is in the `SomeDependency` folder.
//
// ```
// "postActions": [{
//   "Description": "Adding a reference to another project",
//   "ActionId": "B17581D1-C5C9-4489-8F0A-004BE667B814",
//   "ContinueOnError": "false",
//   "ManualInstructions": [{
//     "Text": "Manually add the reference to SomeDependency to MyProjectFile"
//   }],
//   "args": {
//     "targetFiles": ["MyProjectFile.csproj"]
//     "referenceType": "project",
//     "reference": "SomeDependency/SomeDependency.csproj"
//   }
// }]
// ```
//
// ## Adding references to existing projects
//
// It is possible to add references to existing projects in your working directory. Since the name of the existing project is likely not a constant value for all template instantiations, a symbol can be used to pass the name of the existing project.
//
// The example below demonstrates how to add the existing project ```src/AlreadyExisting/AlreadyExisting.csproj``` as a reference to the template source project ```Project1/Project1.csproj```.
//
// ```
// {
//   "symbols": {
//     "existingProject": {
//       ...
//       "type": "parameter",
//       "datatype": "string",
//       "defaultValue": "ExistingProject/ExistingProject.csproj",
//       "fileRename": "ExistingProjectPath" // Must be same as targetFile
//     }
//   },
//   "postActions": [
//     {
//       "Description": "Add ProjectReference to ExistingProject/ExistingProject.csproj",
//       "applyFileRenamesToArgs": [
//         "targetFiles" // Must be specified
//       ],
//       "args": {
//         "targetFiles": [
//           "ExistingProjectPath" // Must be same as fileRename
//         ],
//         "referenceType": "project",
//         "reference": "Project1/Project1.csproj"
//       }
//     }]
// }
// ```
//
// The template above:
// - Configures the ```existingProject``` parameter *symbol* with a ```fileRename``` configuration. 
// - Instructs the *'add reference to a project file'* post action to apply ```fileRename``` to the ```targetFiles``` argument.
// - Uses the value passed to the ```existingProject``` *symbol* to replace the value of the matching  ```targetFiles```.
//
// This template can be instantiated using:
//
// ```dotnet new [templateName] --existingProject src/AlreadyExisting/AlreadyExisting.csproj```
//
//