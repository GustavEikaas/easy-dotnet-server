using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Framework;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class CompatBuildCommand : AsyncCommand<CompatBuildCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandArgument(0, "<PROJECT>")]
    [Description("Path to the project file (.csproj).")]
    public string ProjectPath { get; init; } = string.Empty;

    [CommandOption("--msbuild <PATH>")]
    [Description("Path to MSBuild.exe.")]
    public string MsBuildPath { get; init; } = string.Empty;
  }

  public override ValidationResult Validate(CommandContext context, Settings settings)
  {
    if (string.IsNullOrWhiteSpace(settings.ProjectPath))
      return ValidationResult.Error("Missing required <PROJECT> argument.");

    if (string.IsNullOrWhiteSpace(settings.MsBuildPath))
      return ValidationResult.Error("Missing required --msbuild option.");

    if (!File.Exists(settings.ProjectPath))
      return ValidationResult.Error($"Project file not found: {settings.ProjectPath}");

    if (!File.Exists(settings.MsBuildPath))
      return ValidationResult.Error($"MSBuild not found: {settings.MsBuildPath}");

    return ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] Building project: [cyan]{settings.ProjectPath}[/]");
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] Using MSBuild: [grey]{settings.MsBuildPath}[/]");

    var exitCode = await CompatCommandHandler.RunBuildWithSpectreAsync(
        settings.MsBuildPath,
        settings.ProjectPath
    );

    if (exitCode == 0)
      AnsiConsole.MarkupLine("[green]✔ Build succeeded![/]");
    else
      AnsiConsole.MarkupLine($"[red]✖ Build failed with exit code {exitCode}![/]");

    return exitCode;
  }
}