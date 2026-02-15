using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public class AddProjectsToSolutionFilePostActionHandler(ISolutionService solutionService, IClientService clientService) : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("D396686C-DE0E-4DE6-906D-291CD29FC5DE");
  public Guid ActionId => Id;

  public async Task<bool> Handle(
        IPostAction postAction,
        IReadOnlyList<ICreationPath> primaryOutputs,
        string workingDirectory,
        CancellationToken cancellationToken)
  {
    var slnFilePath = clientService?.ProjectInfo?.SolutionFile;
    if (string.IsNullOrWhiteSpace(slnFilePath))
    {
      return true;
    }

    var projectFiles = GetProjectFiles(postAction, primaryOutputs, workingDirectory);

    try
    {
      foreach (var projectPath in projectFiles)
      {
        await solutionService.AddProjectToSolutionAsync(
            slnFilePath,
            projectPath,
            cancellationToken);
      }

      return true;
    }
    catch
    {
      return false;
    }
  }

  private static List<string> GetProjectFiles(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory)
  {
    var args = postAction.Args;
    var projectFiles = new List<string>();

    if (args.TryGetValue("projectFiles", out var projectFilesRaw) &&
        !string.IsNullOrWhiteSpace(projectFilesRaw))
    {
      var files = projectFilesRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      foreach (var file in files)
      {
        var fullPath = Path.IsPathRooted(file)
            ? file
            : Path.Combine(workingDirectory, file);
        projectFiles.Add(fullPath);
      }
    }
    else if (args.TryGetValue("primaryOutputIndexes", out var indexesRaw) && !string.IsNullOrWhiteSpace(indexesRaw))
    {
      var indexes = indexesRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse);

      foreach (var index in indexes)
      {
        if (index >= 0 && index < primaryOutputs.Count)
        {
          projectFiles.Add(primaryOutputs[index].Path);
        }
      }
    }
    else
    {
      foreach (var output in primaryOutputs)
      {
        projectFiles.Add(output.Path);
      }
    }

    return projectFiles;
  }
}


// # Add project(s) to a solution file
//
//  - **Action ID** : `D396686C-DE0E-4DE6-906D-291CD29FC5DE`
//  - **Specific Configuration** :
//    - `args`:
//      - `projectFiles` (string|array) (optional): 
//         - `string`: A semicolon delimited list of files that should be added to solution. If not specified, primary outputs will be used instead.
//         - `array`: An array of files that should be added to solution. If not specified, primary outputs will be used instead.
//      - `primaryOutputIndexes` (string) (optional): A semicolon delimited list of indexes to the primary outputs. If not specified, all primary outputs will be added. Note: If primary outputs are conditional, multiple post actions with the same conditions as the primary outputs might be necessary.
//      - `solutionFolder` (string) (optional) (supported in 5.0.200 or higher): the destination solution folder path to add the projects to.
//      - `inRoot` (boolean) (optional) (supported in 7.0.200 or higher): whether to place the projects in the root of the solution, rather than create a solution folder. Cannot be used with `solutionFolder`.
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//  - **Ignored in**:
//    - `Visual Studio` - the user indicates where to add project explicitly, so post action defined in the template will be ignored.
//
// Note: when using `projectFiles` argument it should contain the path to the file in source template definition, and ignore all the path and filename changes that can happen when instantiating template. For more details, see [the article](Using-Primary-Outputs-for-Post-Actions.md).
//
//
// ### Example
//
// Adds `MyTestProject.csproj` to solution in output directory or its closest parent directory.
//
// ```
// "primaryOutputs": [
//     {
//       "path": "MyTestProject.csproj"        
//     }
// ],
// "postActions": [{
//   "description": "Add projects to solution",
//   "manualInstructions": [ { "text": "Add generated project to solution manually." } ],
//   "args": {
//     "solutionFolder": "src"
//   },
//   "actionId": "D396686C-DE0E-4DE6-906D-291CD29FC5DE",
//   "continueOnError": true
// }]
// ```
//
// Adds `MyTestProject.csproj` to solution in output directory or its closest parent directory (using `projectFiles` argument).
//
// ```
// "postActions": [{
//   "description": "Add projects to solution",
//   "manualInstructions": [ { "text": "Add generated project to solution manually." } ],
//   "args": {
//     "solutionFolder": "src",
//     "projectFiles": ["MyTestProject.csproj"]
//   },
//   "actionId": "D396686C-DE0E-4DE6-906D-291CD29FC5DE",
//   "continueOnError": true
// }]
// ```
//
// Adds `MyTestProject.csproj` in the root of the solution.
//
// ```json
// "primaryOutputs": [{
//     "path": "MyTestProject.csproj"
//   }
// ],
// "postActions": [{
//     "description": "Add projects to solution",
//     "manualInstructions": [{
//         "text": "Add generated project to solution manually."
//       }
//     ],
//     "args": {
//       "inRoot": true
//     },
//     "actionId": "D396686C-DE0E-4DE6-906D-291CD29FC5DE",
//     "continueOnError": true
//   }
// ]
// ```