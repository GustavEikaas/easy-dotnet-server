using System.Diagnostics;
using Microsoft.TemplateEngine.Abstractions;

namespace EasyDotnet.IDE.TemplateEngine.PostActionHandlers;

public sealed class RunScriptPostActionHandler : IPostActionHandler
{
  public static readonly Guid Id = Guid.Parse("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

  public Guid ActionId => Id;

  public async Task Handle(
      IPostAction postAction,
      CancellationToken cancellationToken)
  {
    var config = BuildConfig(postAction);

    using var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = config.Executable,
        Arguments = config.Arguments,
        RedirectStandardOutput = config.RedirectStandardOutput,
        RedirectStandardError = config.RedirectStandardError,
        UseShellExecute = false
      }
    };

    process.Start();

    if (config.RedirectStandardOutput)
      _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);

    if (config.RedirectStandardError)
      _ = await process.StandardError.ReadToEndAsync(cancellationToken);

    await process.WaitForExitAsync(cancellationToken);

    if (process.ExitCode != 0 && !postAction.ContinueOnError)
    {
      throw new InvalidOperationException($"Script exited with code {process.ExitCode}");
    }
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