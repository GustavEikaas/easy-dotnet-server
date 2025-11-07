using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Framework;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class CompatTestCommand : AsyncCommand<CompatTestCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandArgument(0, "<PROJECT>")]
    [Description("Path to the project file (.csproj).")]
    public string ProjectPath { get; init; } = string.Empty;

    [CommandOption("--target <PATH>")]
    [Description("Path to the test assembly (e.g., MyTests.dll).")]
    public string TargetPath { get; init; } = string.Empty;

    [CommandOption("--msbuild <PATH>")]
    [Description("Path to MSBuild.exe.")]
    public string MsBuildPath { get; init; } = string.Empty;

    [CommandOption("--vstest <PATH>")]
    [Description("Path to vstest.console.exe.")]
    public string VsTestPath { get; init; } = string.Empty;
  }

  public override ValidationResult Validate(CommandContext context, Settings settings)
  {
    if (string.IsNullOrWhiteSpace(settings.ProjectPath))
      return ValidationResult.Error("Missing required <PROJECT> argument.");

    if (string.IsNullOrWhiteSpace(settings.MsBuildPath))
      return ValidationResult.Error("Missing required --msbuild option.");

    if (string.IsNullOrWhiteSpace(settings.VsTestPath))
      return ValidationResult.Error("Missing required --vstest option.");

    if (string.IsNullOrWhiteSpace(settings.TargetPath))
      return ValidationResult.Error("Missing required --target option.");

    if (!File.Exists(settings.ProjectPath))
      return ValidationResult.Error($"Project file not found: {settings.ProjectPath}");

    if (!File.Exists(settings.MsBuildPath))
      return ValidationResult.Error($"MSBuild not found: {settings.MsBuildPath}");

    if (!File.Exists(settings.VsTestPath))
      return ValidationResult.Error($"vstest.console.exe not found: {settings.VsTestPath}");

    return ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] Project: [cyan]{settings.ProjectPath}[/]");
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] MSBuild: [grey]{settings.MsBuildPath}[/]");
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] Target: [grey]{settings.TargetPath}[/]");
    AnsiConsole.MarkupLine($"[yellow][[compat]][/] VsTest: [grey]{settings.VsTestPath}[/]");

    AnsiConsole.MarkupLine("[yellow][[compat]][/] Building project before running tests...");

    var buildExit = await CompatCommandHandler.RunBuildWithSpectreAsync(
        settings.MsBuildPath,
        settings.ProjectPath
    );

    if (buildExit != 0)
    {
      return buildExit;
    }

    AnsiConsole.MarkupLine($"[yellow][[compat]][/] Running tests from {settings.TargetPath}...");

    var testExit = await CompatCommandHandler.RunProcessAsync(
        "dotnet",
        $"\"{settings.VsTestPath}\" \"{settings.TargetPath}\""
    );

    if (testExit == 0)
      AnsiConsole.MarkupLine("[green]✔ Tests completed successfully![/]");
    else
      AnsiConsole.MarkupLine($"[red]✖ Tests failed (exit code {testExit}).[/]");

    return testExit;
  }
}