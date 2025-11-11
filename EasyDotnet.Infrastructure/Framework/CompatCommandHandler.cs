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
    var messages = new List<MsBuildStdoutMessage>();
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
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                          var parsed = MsBuildBuildStdoutParser.ParseMsBuildLines(e.Data);
                          messages.AddRange(parsed);
                        }
                      };

          process.ErrorDataReceived += (_, e) =>
          {
            if (!string.IsNullOrEmpty(e.Data))
            {
              var parsed = MsBuildBuildStdoutParser.ParseMsBuildLines(e.Data);
              if (!parsed.Any())
                messages.Add(new MsBuildStdoutMessage("error", "", 0, 0, "", e.Data));
              else
                messages.AddRange(parsed);
            }
          };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
          await process.WaitForExitAsync();

          exitCode = process.ExitCode;
        });


    var errorCount = messages.Count(m => m.Type.Equals("error", StringComparison.OrdinalIgnoreCase));
    var warningCount = messages.Count(m => m.Type.Equals("warning", StringComparison.OrdinalIgnoreCase));

    if (errorCount > 0 || warningCount > 0)
    {
      var table = MsBuildMessageTableBuilder(messages);
      AnsiConsole.Write(table);
    }

    if (exitCode == 0)
      AnsiConsole.MarkupLine($"\n[bold green]✔ Build succeeded![/] [yellow]Warnings: {warningCount}[/]");
    else
      AnsiConsole.MarkupLine($"\n[bold red]✖ Build failed![/] [yellow]Warnings: {warningCount}[/], [red]Errors: {errorCount}[/]");

    return exitCode;
  }

  public static Table MsBuildMessageTableBuilder(IEnumerable<MsBuildStdoutMessage> messages)
  {
    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn("Type");
    table.AddColumn("File");
    table.AddColumn("Line");
    table.AddColumn("Col");
    table.AddColumn("Code");
    table.AddColumn("Message");

    foreach (var msg in messages)
    {
      var color = msg.Type.Equals("error", StringComparison.OrdinalIgnoreCase) ? "red" :
                  msg.Type.Equals("warning", StringComparison.OrdinalIgnoreCase) ? "yellow" : "grey";

      table.AddRow(
          $"[{color}]{msg.Type}[/]",
          $"[{color}]{msg.FilePath}[/]",
          $"[{color}]{msg.LineNumber}[/]",
          $"[{color}]{msg.ColumnNumber}[/]",
          $"[{color}]{msg.Code}[/]",
          $"[{color}]{msg.Message?.EscapeMarkup()}[/]"
      );
    }

    return table;
  }
}

