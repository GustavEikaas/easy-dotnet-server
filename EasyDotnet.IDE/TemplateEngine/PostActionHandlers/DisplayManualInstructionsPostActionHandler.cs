using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class DisplayManualInstructionsPostActionHandler(IEditorService editorService) : IPostActionHandler
{
  public static readonly Guid Id =
      Guid.Parse("AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C");

  public Guid ActionId => Id;

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    var lines = new List<string>();

    if (!string.IsNullOrWhiteSpace(postAction.Description))
    {
      lines.Add($"Description: {postAction.Description}");
    }

    if (postAction.ManualInstructions is not null)
    {
      lines.Add($"Manual instructions: {postAction.ManualInstructions}");
    }

    if (TryBuildCommand(postAction, out var command))
    {
      lines.Add($"Actual command: {command}");
    }

    if (lines.Count > 0)
    {
      await editorService.DisplayMessage(string.Join(Environment.NewLine, lines));
    }

    return true;
  }

  private static bool TryBuildCommand(
      IPostAction postAction,
      out string command)
  {
    command = string.Empty;

    if (!postAction.Args.TryGetValue("executable", out var executable) ||
        string.IsNullOrWhiteSpace(executable))
    {
      return false;
    }

    postAction.Args.TryGetValue("args", out var args);

    command = string.IsNullOrWhiteSpace(args)
        ? executable
        : $"{executable} {args}";

    return true;
  }
}


// # Display manual instructions
//
// Prints out the manual instructions after instantiating template in format:
// ```
// Description: <description defined in post action>
// Manual instructions: <manual instructions defined in post action>
// Actual command: <executable> <args>
// ```
//
// Command is printed only if defined in post action arguments.
//
//  - **Action ID** : `AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C`
//  - **Specific Configuration** :
//     - `args`: 
//      - `executable` (string) (optional): command to run
//      - `args` (string) (optional): arguments to use
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//
// ### Example
//
// ```
// "postActions": [{
//   "description": "Manual actions required",
//   "manualInstructions": [{
//     "text": "Run the following command"
//   }],
//   "actionId": "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C",
//   "args": {
//     "executable": "setup.cmd",
//     "args": "<your project name>"
//   },
//   "continueOnError": true
// }]
// ```
//