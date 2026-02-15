using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class OpenFilePostActionHandler(
    IEditorService editorService)
    : IPostActionHandler
{
  public static readonly Guid Id =
      Guid.Parse("84C0DA21-51C8-4541-9940-6CA19AF04EE6");

  public Guid ActionId => Id;

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    if (!postAction.Args.TryGetValue("files", out var rawFiles) ||
        string.IsNullOrWhiteSpace(rawFiles))
    {
      return false;
    }

    foreach (var index in ParseIndices(rawFiles))
    {
      if (index < 0 || index >= primaryOutputs.Count)
        return false;

      var relativePath = primaryOutputs[index].Path;

      var fullPath = Path.Combine(workingDirectory, relativePath);

      await editorService.RequestOpenBuffer(fullPath);
    }

    return true;
  }

  private static IEnumerable<int> ParseIndices(string raw) => raw
        .Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s.Trim(), out var i) ? i : -1);
}


// # Open a file in the editor
//
// Opens a file in the editor. For command line cases this post action will be ignored.
//
//  - **Action ID** : `84C0DA21-51C8-4541-9940-6CA19AF04EE6`
//  - **Specific Configuration** :
//    - `files` (string): A semicolon delimited list of indexes to the primary outputs.
//      Note: If primary outputs are conditional, multiple post actions with the same
//      conditions as the primary outputs might be necessary.
//  - **Supported in**:
//    - since `Visual Studio 2017.3 Preview 1`
//
// ### Example
//
// ```
// "primaryOutputs": [
//   { "path": "Company.ClassLibrary1.csproj" },
//   {
//     "condition": "(HostIdentifier != \"dotnetcli\")",
//     "path": "Class1.cs"
//   }
// ],
// "postActions": [
//   {
//     "condition": "(HostIdentifier != \"dotnetcli\")",
//     "description": "Opens Class1.cs in the editor",
//     "manualInstructions": [ ],
//     "actionId": "84C0DA21-51C8-4541-9940-6CA19AF04EE6",
//     "args": {
//       "files": "1"
//     },
//     "continueOnError": true
//   }
// ]
// ```