using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Infrastructure.Framework;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class CompatRunIisCommand : AsyncCommand<CompatRunIisCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandArgument(0, "<PROJECT>")]
    [Description("Path to the project file (.csproj).")]
    public string ProjectPath { get; init; } = string.Empty;

    [CommandOption("--msbuild <PATH>")]
    [Description("Path to MSBuild.exe.")]
    public string MsBuildPath { get; init; } = string.Empty;

    [CommandOption("--iis-exe <PATH>")]
    [Description("Path to IIS Express executable.")]
    public string IisExe { get; init; } = string.Empty;

    [CommandOption("--config <PATH>")]
    [Description("Path to IIS configuration file.")]
    public string ConfigPath { get; init; } = string.Empty;

    [CommandOption("--site <NAME>")]
    [Description("Site name for IIS Express.")]
    public string SiteName { get; init; } = string.Empty;
  }

  public override ValidationResult Validate(CommandContext context, Settings settings)
  {
    if (string.IsNullOrWhiteSpace(settings.ProjectPath))
      return ValidationResult.Error("Missing required <PROJECT> argument.");

    if (string.IsNullOrWhiteSpace(settings.MsBuildPath))
      return ValidationResult.Error("Missing required --msbuild option.");

    if (string.IsNullOrWhiteSpace(settings.IisExe))
      return ValidationResult.Error("Missing required --iis-exe option.");

    if (string.IsNullOrWhiteSpace(settings.ConfigPath))
      return ValidationResult.Error("Missing required --config option.");

    if (string.IsNullOrWhiteSpace(settings.SiteName))
      return ValidationResult.Error("Missing required --site option.");

    if (!File.Exists(settings.ProjectPath))
      return ValidationResult.Error($"Project file not found: {settings.ProjectPath}");

    if (!File.Exists(settings.MsBuildPath))
      return ValidationResult.Error($"MSBuild not found: {settings.MsBuildPath}");

    if (!File.Exists(settings.IisExe))
      return ValidationResult.Error($"IIS Express executable not found: {settings.IisExe}");

    if (!File.Exists(settings.ConfigPath))
      return ValidationResult.Error($"IIS configuration file not found: {settings.ConfigPath}");

    return ValidationResult.Success();
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var buildExit = await CompatCommandHandler.RunBuildWithSpectreAsync(settings.MsBuildPath, settings.ProjectPath);
    if (buildExit != 0)
    {
      return buildExit;
    }

    var argsLine = $"/config:\"{settings.ConfigPath}\" /site:\"{settings.SiteName}\"";
    return await CompatCommandHandler.RunProcessAsync(settings.IisExe, argsLine);
  }
}