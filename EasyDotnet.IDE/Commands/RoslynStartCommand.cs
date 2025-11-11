using EasyDotnet.Infrastructure.Services;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasyDotnet.IDE.Commands;

public sealed class RoslynStartCommand : AsyncCommand<RoslynStartCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [Description("Enable Roslynator analyzers (optional).")]
    [CommandOption("--roslynator")]
    public bool UseRoslynator { get; init; }

    [Description("Additional analyzer assemblies to load.")]
    [CommandOption("--analyzer <PATH>")]
    public string[] AnalyzerAssemblies { get; init; } = [];
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var roslynLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDotnet", "RoslynLogs");
    Directory.CreateDirectory(roslynLogDir);

    var roslynDllPath = RoslynLocator.GetRoslynDllPath();

    var startInfo = new ProcessStartInfo("dotnet")
    {
      UseShellExecute = false,
    };

    startInfo.ArgumentList.Add(roslynDllPath);
    startInfo.ArgumentList.Add("--stdio");
    startInfo.ArgumentList.Add("--logLevel=Information");
    startInfo.ArgumentList.Add("--extensionLogDirectory");
    startInfo.ArgumentList.Add(roslynLogDir);

    if (settings.UseRoslynator)
    {
      foreach (var analyzer in RoslynLocator.GetRoslynatorAnalyzers())
      {
        startInfo.ArgumentList.Add("--extension");
        startInfo.ArgumentList.Add(analyzer);
      }
    }

    foreach (var dll in settings.AnalyzerAssemblies)
    {
      startInfo.ArgumentList.Add("--extension");
      startInfo.ArgumentList.Add(dll);
    }

    var process = new Process { StartInfo = startInfo };

    process.Start();
    await process.WaitForExitAsync(cancellationToken);

    return process.ExitCode;
  }
}