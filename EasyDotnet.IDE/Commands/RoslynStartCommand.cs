using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Services;
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

    [Description("Enable Roslynator analyzers (optional).")]
    [CommandOption("--roslynator")]
    public bool UseRoslynator { get; init; }

    [Description("Additional analyzer assemblies to load.")]
    [CommandOption("--analyzer <PATH>")]
    public string[] AnalyzerAssemblies { get; init; } = [];

    [Description("Full path to the Roslyn dependency used with DevKit (optional).")]
    [CommandOption("--devKitDependencyPath <PATH>")]
    public string? DevKitDependencyPath { get; init; }

    [Description("Full path to the Razor source generator (optional).")]
    [CommandOption("--razorSourceGenerator <PATH>")]
    public string? RazorSourceGenerator { get; init; }

    [Description("Full path to the Razor design time target path (optional).")]
    [CommandOption("--razorDesignTimePath <PATH>")]
    public string? RazorDesignTimePath { get; init; }

    [Description("Full path to the C# design time target path (optional).")]
    [CommandOption("--csharpDesignTimePath <PATH>")]
    public string? CSharpDesignTimePath { get; init; }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var roslynDllPath = RoslynLocator.GetRoslynDllPath();
    if (settings.ShowVersion)
    {
      return await ShowRoslynVersion(roslynDllPath, cancellationToken);
    }

    if (!CheckRequiredDotnetSdk())
    {
      return RoslynExitCodes.SDKOutdated;
    }

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

    if (!string.IsNullOrWhiteSpace(settings.RazorSourceGenerator))
    {
      startInfo.ArgumentList.Add("--razorSourceGenerator");
      startInfo.ArgumentList.Add(settings.RazorSourceGenerator);
    }

    if (!string.IsNullOrWhiteSpace(settings.RazorDesignTimePath))
    {
      startInfo.ArgumentList.Add("--razorDesignTimePath");
      startInfo.ArgumentList.Add(settings.RazorDesignTimePath);
    }

    if (!string.IsNullOrWhiteSpace(settings.CSharpDesignTimePath))
    {
      startInfo.ArgumentList.Add("--csharpDesignTimePath");
      startInfo.ArgumentList.Add(settings.CSharpDesignTimePath);
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

  private static class RoslynExitCodes {
    public const int SDKOutdated = 75;
  }
}