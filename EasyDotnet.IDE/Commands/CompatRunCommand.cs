using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Framework;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class CompatRunCommand : AsyncCommand<CompatRunCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandArgument(0, "<PROJECT>")]
    [Description("Path to the project file (.csproj).")]
    public string ProjectPath { get; init; } = string.Empty;

    [CommandOption("--msbuild <PATH>")]
    [Description("Path to MSBuild.exe.")]
    public string MsBuildPath { get; init; } = string.Empty;

    [CommandOption("--target <PATH>")]
    [Description("Path to compiled target executable.")]
    public string TargetPath { get; init; } = string.Empty;
  }
  public override ValidationResult Validate(CommandContext context, Settings settings)
  {
    if (string.IsNullOrWhiteSpace(settings.ProjectPath))
      return ValidationResult.Error("Missing required <PROJECT> argument.");

    if (string.IsNullOrWhiteSpace(settings.MsBuildPath))
      return ValidationResult.Error("Missing required --msbuild option.");

    if (string.IsNullOrWhiteSpace(settings.TargetPath))
      return ValidationResult.Error("Missing required --target option.");

    if (!File.Exists(settings.ProjectPath))
      return ValidationResult.Error($"Project file not found: {settings.ProjectPath}");

    if (!File.Exists(settings.MsBuildPath))
      return ValidationResult.Error($"MSBuild not found: {settings.MsBuildPath}");

    return ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var buildExit = await CompatCommandHandler.RunBuildWithSpectreAsync(settings.MsBuildPath, settings.ProjectPath);
    if (buildExit != 0)
    {
      Console.Error.WriteLine($"[compat] Build failed (exit code {buildExit}). Aborting run.");
      return buildExit;
    }

    if (!File.Exists(settings.TargetPath))
    {
      Console.Error.WriteLine($"[compat] Target executable not found: {settings.TargetPath}");
      return 1;
    }
    return await CompatCommandHandler.RunProcessAsync(settings.TargetPath, "");
  }
}