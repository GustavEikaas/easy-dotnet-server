using System.Diagnostics;
using EasyDotnet.MsBuild;
using Spectre.Console;

namespace EasyDotnet.Infrastructure.Framework;

public static class CompatCommandHandler
{
  private const string Shim = "dotnet easydotnet compat";

  public static string GetBuildCommand(string projectPath, string msbuildPath) => $"{Shim} build \"{projectPath}\" --msbuild \"{msbuildPath}\"";
  public static string GetTestCommand(string projectPath, string targetPath, string msbuildPath, string vstestPath) => $"{Shim} test \"{projectPath}\" --target \"{targetPath}\" --msbuild \"{msbuildPath}\" --vstest \"{vstestPath}\"";
  public static string GetIisCommand(
          string projectPath,
          string msbuildPath,
          string iisExe,
          string configPath,
          string siteName)
          => $"{Shim} run-iis \"{projectPath}\" " +
             $"--msbuild \"{msbuildPath}\" " +
             $"--iis-exe \"{iisExe}\" " +
             $"--config \"{configPath}\" " +
             $"--site \"{siteName}\"";

  public static string GetRunCommand(
      string projectPath,
      string msbuildPath,
      string targetPath)
      => $"{Shim} run \"{projectPath}\" " +
         $"--msbuild \"{msbuildPath}\" " +
         $"--target \"{targetPath}\"";


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

          process.OutputDataReceived += (_, e) =>
          {
            if (string.IsNullOrEmpty(e.Data))
              return;

            var msg = MsBuildBuildStdoutParser.ParseMsBuildLines(e.Data).FirstOrDefault();

            if (msg != null)
            {
              if (msg.Type.Equals("error", StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[red]{msg.Message.EscapeMarkup()}[/]");
              else if (msg.Type.Equals("warning", StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[yellow]{msg.Message.EscapeMarkup()}[/]");
              else
                AnsiConsole.MarkupLine($"[grey]{e.Data.EscapeMarkup()}[/]");
            }
            else
            {
              AnsiConsole.MarkupLine($"[grey]{e.Data.EscapeMarkup()}[/]");
            }
          };

          process.ErrorDataReceived += (_, e) => { if (e.Data != null) AnsiConsole.MarkupLine($"[red]{e.Data.EscapeMarkup()}[/]"); };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
          await process.WaitForExitAsync();

          exitCode = process.ExitCode;
        });

    if (exitCode == 0)
      AnsiConsole.MarkupLine("[bold green]✔ Build succeeded![/]");
    else
      AnsiConsole.MarkupLine("[bold red]✖ Build failed![/]");

    return exitCode;
  }
}