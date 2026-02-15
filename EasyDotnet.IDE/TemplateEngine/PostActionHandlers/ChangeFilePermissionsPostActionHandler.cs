using System.Diagnostics;
using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class ChangeFilePermissionsPostActionHandler(
    IEditorService editorService)
    : IPostActionHandler
{
  public static readonly Guid Id =
      Guid.Parse("CB9A6CF3-4F5C-4860-B9D2-03A574959774");

  public Guid ActionId => Id;

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    if (OperatingSystem.IsWindows())
    {
      return true;
    }

    if (postAction.Args == null || postAction.Args.Count == 0)
      return false;

    var success = true;

    foreach (var (mode, value) in postAction.Args)
    {
      foreach (var file in ResolveTargets(value, workingDirectory))
      {
        var result = await RunChmodAsync(
            mode,
            file,
            workingDirectory,
            cancellationToken);

        if (!result)
        {
          success = false;
        }
      }
    }

    return success;
  }

  private static IEnumerable<string> ResolveTargets(
      string rawValue,
      string workingDirectory)
  {
    if (rawValue.StartsWith('['))
    {
      var items = System.Text.Json.JsonSerializer
          .Deserialize<string[]>(rawValue) ?? [];

      return items.SelectMany(i => ExpandGlob(i, workingDirectory));
    }

    return ExpandGlob(rawValue, workingDirectory);
  }

  private static string[] ExpandGlob(
      string pattern,
      string workingDirectory)
  {
    if (pattern.Contains('*') || pattern.Contains('?'))
    {
      return Directory.GetFiles(
          workingDirectory,
          pattern,
          SearchOption.TopDirectoryOnly);
    }

    var fullPath = Path.Combine(workingDirectory, pattern);

    return File.Exists(fullPath)
        ? [fullPath]
        : [];
  }

  private async Task<bool> RunChmodAsync(
      string mode,
      string file,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    var psi = new ProcessStartInfo
    {
      FileName = "/bin/sh",
      Arguments = $"-c \"chmod {mode} {file}\"",
      WorkingDirectory = workingDirectory,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false
    };

    using var process = new Process { StartInfo = psi };

    process.Start();

    var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode == 0)
    {
      return true;
    }

    if (!string.IsNullOrWhiteSpace(stderr))
    {
      await editorService.DisplayError(stderr);
    }

    return false;
  }
}


// # Change file permissions
//
// Unix / OS X only (runs the Unix `chmod` command).
//
//  - **Action ID** : `CB9A6CF3-4F5C-4860-B9D2-03A574959774`
//  - **Specific Configuration** :
//     - `args`: The permissions to set (see examples). Usually this will contain a glob like `{ "+x": "*.sh" }` or a list of filenames like `{ "+x": ["script1", "script2"] }`.
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//
// ### Example
//
// ```
// "postActions": [{
//   "condition": "(OS != \"Windows_NT\")",
//   "description": "Make scripts executable",
//   "manualInstructions": [{
//     "text": "Run 'chmod +x *.sh'"
//   }],
//   "actionId": "cb9a6cf3-4f5c-4860-b9d2-03a574959774",
//   "args": {
//     "+x": "*.sh"
//   },
//   "continueOnError": true
// }]
// ```
//
// or
//
// ```
// "postActions": [{
//   "condition": "(OS != \"Windows_NT\")",
//   "description": "Make scripts executable",
//   "manualInstructions": [ { "text": "Run 'chmod +x *.sh somethingelse'" }  ],
//   "actionId": "cb9a6cf3-4f5c-4860-b9d2-03a574959774",
//   "args": {
//     "+x": [
//       "*.sh",
//       "somethingelse"
//     ]
//   },
//   "continueOnError": true
// }]
// ```
//