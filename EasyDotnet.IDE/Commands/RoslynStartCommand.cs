using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.IDE.Services;
using Microsoft.Build.Locator;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class RoslynStartCommand : AsyncCommand<RoslynStartCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [Description("Show the Roslyn version and exit.")]
    [CommandOption("--version")]
    public bool ShowVersion { get; init; }

    [Description("Use EasyDotnet analyzers (optional).")]
    [CommandOption("--easy-dotnet-analyzer")]
    public bool UseEasyDotnetAnalyzer { get; init; }

    [Description("Enable Roslynator analyzers (optional).")]
    [CommandOption("--roslynator")]
    public bool UseRoslynator { get; init; }

    [Description("Additional analyzer assemblies to load.")]
    [CommandOption("--analyzer <PATH>")]
    public string[] AnalyzerAssemblies { get; init; } = [];

    [Description("Full path to the Roslyn dependency used with DevKit (optional).")]
    [CommandOption("--devKitDependencyPath <PATH>")]
    public string? DevKitDependencyPath { get; init; }

    [Description("Client process id Roslyn should monitor for shutdown.")]
    [CommandOption("--clientProcessId <PID>")]
    public int? ClientProcessId { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var customRoslynDllPath = RoslynLocator.GetCustomRoslynDllPath();
    if (!string.IsNullOrWhiteSpace(customRoslynDllPath))
    {
      if (settings.ShowVersion)
      {
        return await ShowRoslynVersion(customRoslynDllPath, cancellationToken);
      }

      return await StartRoslynDllAsync(customRoslynDllPath, settings, cancellationToken);
    }

    if (settings.ShowVersion)
    {
      var status = await RoslynToolService.GetStatusAsync(cancellationToken);
      if (!status.IsInstalled)
      {
        AnsiConsole.MarkupLine($"[red]{status.Message}[/]");
        return 1;
      }

      AnsiConsole.MarkupLine($"[green]Roslyn Version:[/] {status.Version}");
      return 0;
    }

    if (!CheckRequiredDotnetSdk())
    {
      return RoslynExitCodes.SDKOutdated;
    }

    var toolStatus = await RoslynToolService.EnsureInstalledAsync(true, cancellationToken);
    if (!toolStatus.IsInstalled || string.IsNullOrWhiteSpace(toolStatus.ExecutablePath))
    {
      AnsiConsole.MarkupLine($"[red]Failed to locate {RoslynToolService.PackageId}.[/]");
      if (!string.IsNullOrWhiteSpace(toolStatus.Message))
      {
        AnsiConsole.WriteLine(toolStatus.Message);
      }
      AnsiConsole.MarkupLine($"Install it with: [grey]dotnet tool install --global {RoslynToolService.PackageId} --prerelease[/]");
      return RoslynExitCodes.ToolMissing;
    }

    if (toolStatus.IsBelowRecommended)
    {
      Console.Error.WriteLine($"[easy-dotnet] {toolStatus.Message}. Update with: dotnet tool update --global {RoslynToolService.PackageId} --prerelease");
    }

    return await StartRoslynToolAsync(toolStatus.ExecutablePath, settings, cancellationToken);
  }

  private static async Task<int> StartRoslynToolAsync(string executablePath, Settings settings, CancellationToken cancellationToken)
  {
    var roslynLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDotnet", "RoslynLogs");
    Directory.CreateDirectory(roslynLogDir);

    List<string> arguments = ["--stdio", "--logLevel=Information", "--extensionLogDirectory", roslynLogDir];

    if (settings.UseRoslynator)
    {
      foreach (var analyzer in RoslynLocator.GetRoslynatorAnalyzers())
      {
        arguments.Add("--extension");
        arguments.Add(analyzer);
      }
    }
    if (settings.UseEasyDotnetAnalyzer)
    {
      foreach (var analyzer in RoslynLocator.GetEasyDotnetAnalyzers())
      {
        arguments.Add("--extension");
        arguments.Add(analyzer);
      }
    }

    foreach (var dll in settings.AnalyzerAssemblies)
    {
      arguments.Add("--extension");
      arguments.Add(dll);
    }

    if (!string.IsNullOrWhiteSpace(settings.DevKitDependencyPath))
    {
      arguments.Add("--devKitDependencyPath");
      arguments.Add(settings.DevKitDependencyPath);
    }

    if (settings.ClientProcessId is > 0)
    {
      arguments.Add("--clientProcessId");
      arguments.Add(settings.ClientProcessId.Value.ToString());
    }

    var startInfo = RoslynToolService.CreateProcessStartInfo(executablePath, arguments, redirectOutput: false);
    var process = new Process { StartInfo = startInfo };

    process.Start();
    await process.WaitForExitAsync(cancellationToken);

    return process.ExitCode;
  }

  private static async Task<int> StartRoslynDllAsync(string roslynDllPath, Settings settings, CancellationToken cancellationToken)
  {
    var roslynLogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDotnet", "RoslynLogs");
    Directory.CreateDirectory(roslynLogDir);

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
    if (settings.UseEasyDotnetAnalyzer)
    {
      foreach (var analyzer in RoslynLocator.GetEasyDotnetAnalyzers())
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

    if (!string.IsNullOrWhiteSpace(settings.DevKitDependencyPath))
    {
      startInfo.ArgumentList.Add("--devKitDependencyPath");
      startInfo.ArgumentList.Add(settings.DevKitDependencyPath);
    }

    if (settings.ClientProcessId is > 0)
    {
      startInfo.ArgumentList.Add("--clientProcessId");
      startInfo.ArgumentList.Add(settings.ClientProcessId.Value.ToString());
    }

    var process = new Process { StartInfo = startInfo };

    process.Start();
    await process.WaitForExitAsync(cancellationToken);

    return process.ExitCode;
  }

  public static async Task<int> ShowRoslynVersion(string roslynDllPath, CancellationToken cancellationToken)
  {
    var versionInfo = new ProcessStartInfo("dotnet")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    versionInfo.ArgumentList.Add(roslynDllPath);
    versionInfo.ArgumentList.Add("--version");

    var proc = new Process { StartInfo = versionInfo };

    proc.Start();

    var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
    var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);

    await proc.WaitForExitAsync(cancellationToken);
    if (proc.ExitCode == 0)
    {
      AnsiConsole.MarkupLine($"[green]Roslyn Version:[/] {stdout.Trim()}");
    }
    else
    {
      AnsiConsole.WriteException(new Exception(stderr));
    }
    return proc.ExitCode;
  }

  private static bool CheckRequiredDotnetSdk()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    return MSBuildLocator.QueryVisualStudioInstances().Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk).Any(x => x.Version >= new Version(10, 0));
  }

  private static class RoslynExitCodes
  {
    public const int ToolMissing = 74;
    public const int SDKOutdated = 75;
  }
}

public sealed class RoslynToolInstallCommand : AsyncCommand<RoslynToolInstallCommand.Settings>
{
  public sealed class Settings : CommandSettings { }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var result = await RoslynToolService.InstallAsync(cancellationToken);
    WriteCommandResult(result);
    return result.ExitCode;
  }

  private static void WriteCommandResult(RoslynToolCommandResult result)
  {
    if (!string.IsNullOrWhiteSpace(result.Output))
    {
      AnsiConsole.WriteLine(result.Output);
    }
  }
}

public sealed class RoslynToolUpdateCommand : AsyncCommand<RoslynToolUpdateCommand.Settings>
{
  public sealed class Settings : CommandSettings { }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var result = await RoslynToolService.UpdateAsync(cancellationToken);
    if (!string.IsNullOrWhiteSpace(result.Output))
    {
      AnsiConsole.WriteLine(result.Output);
    }
    return result.ExitCode;
  }
}
