using System.CommandLine.Parsing;
using System.Diagnostics;
using EasyDotnet.Application.Interfaces;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class RunScriptPostActionHandler(IEditorService editorService, IEditorProcessManagerService editorProcessManagerService) : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

  public Guid ActionId => Id;

  public async Task<bool> Handle(
      IPostAction postAction,
      IReadOnlyList<ICreationPath> primaryOutputs,
      string workingDirectory,
      CancellationToken cancellationToken)
  {
    var config = BuildConfig(postAction);


    if (!config.RedirectStandardOutput)
    {
      var processId = await editorService.RequestRunCommand(new(config.Executable, [.. CommandLineParser.SplitCommandLine(config.Arguments)], workingDirectory, []));
      var exitCode = await editorProcessManagerService.WaitForExitAsync(processId);
      return exitCode == 0;
    }

    var psi = new ProcessStartInfo
    {
      FileName = config.Executable,
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = config.RedirectStandardOutput,
      RedirectStandardError = config.RedirectStandardError,
      UseShellExecute = false
    };

    foreach (var arg in CommandLineParser.SplitCommandLine(config.Arguments))
    {
      psi.ArgumentList.Add(arg);
    }

    using var process = new Process { StartInfo = psi };

    process.Start();

    var stderr = config.RedirectStandardError ? await process.StandardError.ReadToEndAsync(cancellationToken) : null;

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode == 0)
    {
      return true;

    }
    else if (stderr is not null)
    {
      await editorService.DisplayError(stderr);
    }

    return false;
  }

  private static RunScriptConfig BuildConfig(IPostAction postAction)
  {
    var args = postAction.Args;

    if (!args.TryGetValue("executable", out var executable) ||
        string.IsNullOrWhiteSpace(executable))
    {
      throw new InvalidOperationException("Missing required argument: executable");
    }

    if (!args.TryGetValue("args", out var arguments))
    {
      throw new InvalidOperationException("Missing required argument: args");
    }

    var redirectStdOut = !args.TryGetValue("redirectStandardOutput", out var stdoutRaw) || stdoutRaw != "false";

    var redirectStdErr = !args.TryGetValue("redirectStandardError", out var stderrRaw) || stderrRaw != "false";

    return new RunScriptConfig(
        executable,
        arguments,
        redirectStdOut,
        redirectStdErr);
  }

  private sealed record RunScriptConfig(
      string Executable,
      string Arguments,
      bool RedirectStandardOutput,
      bool RedirectStandardError);
}

// # Run script
//
// Used to run a script after create.
//
//  - **Action ID** : `3A7C4B45-1F5D-4A30-959A-51B88E82B5D2`
//  - **Specific Configuration** : There are three required properties that must be specified.
//    - `args`
//      - `executable` (string): The executable to launch.
//      - `args` (string): The arguments to pass to the executable.
//      - `redirectStandardOutput` (bool) (optional): Whether or not to redirect stdout for the process (prevents output from being displayed if true). The default value is true.
//      - `redirectStandardError` (bool) (optional): Defines whether or not the stderr should be redirected. If the output is redirected, it prevents it from being displayed. The default value is true. Available since .NET SDK 6.0.100.
//    - `manualInstructions` (required)
//  - **Supported in**:
//    - `dotnet new3`
//    - `dotnet new` (2.0.0 or higher)
//
// The working directory for the launched executable is set to the root of the output template content.
//
// ### Example
//
// ```
// "postActions": [{
//   "actionId": "3A7C4B45-1F5D-4A30-959A-51B88E82B5D2",
//   "args": {
//     "executable": "setup.cmd",
//     "args": "",
//     "redirectStandardOutput": false,
//     "redirectStandardError": false
//   },
//   "manualInstructions": [{
//      "text": "Run 'setup.cmd'"
//   }],
//   "continueOnError": false,
//   "description ": "setups the project by calling setup.cmd"
// }]
// ```
//