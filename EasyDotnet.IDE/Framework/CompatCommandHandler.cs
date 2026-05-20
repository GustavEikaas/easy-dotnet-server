using System.Diagnostics;
using Spectre.Console;

namespace EasyDotnet.IDE.Framework;

public static class CompatCommandHandler
{
  public static async Task<int> RunProcessAsync(string fileName, string arguments)
  {
    using var process = new System.Diagnostics.Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = false
      }
    };

    process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    await process.WaitForExitAsync();

    return process.ExitCode;
  }

  public static async Task<int> RunBuildWithSpectreAsync(string msbuildPath, string projectPath)
  {
    if (!File.Exists(msbuildPath))
      throw new FileNotFoundException("MSBuild not found.", msbuildPath);

    if (!File.Exists(projectPath))
      throw new FileNotFoundException("Project file not found.", projectPath);

    var exitCode = 0;
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[yellow]Building project...[/]", async ctx =>
        {
          var process = new System.Diagnostics.Process
          {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
              FileName = msbuildPath,
              Arguments = $"\"{projectPath}\" /nologo /m /verbosity:minimal",
              RedirectStandardOutput = true,
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };

          process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
          process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
          await process.WaitForExitAsync();

          exitCode = process.ExitCode;
        });


    if (exitCode == 0)
      AnsiConsole.MarkupLine("\n[bold green]Build succeeded.[/]");
    else
      AnsiConsole.MarkupLine($"\n[bold red]Build failed.[/] [red]Exit code: {exitCode}[/]");

    return exitCode;
  }
}
