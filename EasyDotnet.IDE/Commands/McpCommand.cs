using System.ComponentModel;
using System.Diagnostics;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.IDE.Mcp;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class McpCommand : AsyncCommand<McpCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandOption("--solution <PATH>")]
    [Description("Solution the tools work on. Defaults to the single .sln/.slnx in the working directory.")]
    public string? Solution { get; init; }

    [CommandOption("--logLevel <LEVEL>")]
    [Description("Logging verbosity, written to stderr (Off, Critical, Error, Warning, Information, Verbose, All). Default: Off")]
    [DefaultValue(SourceLevels.Off)]
    public SourceLevels LogLevel { get; init; } = SourceLevels.Off;
  }

  public override ValidationResult Validate(CommandContext context, Settings settings) =>
      settings.Solution is not null && !File.Exists(settings.Solution)
          ? ValidationResult.Error($"Solution not found: {settings.Solution}")
          : ValidationResult.Success();

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var rootDir = Directory.GetCurrentDirectory();
    string? solution;
    try
    {
      solution = settings.Solution is not null ? Path.GetFullPath(settings.Solution) : FindSolution(rootDir);
    }
    catch (InvalidOperationException ex)
    {
      await Console.Error.WriteLineAsync(ex.Message);
      return 1;
    }

    // stdout carries the MCP protocol; route any stray managed console output to stderr.
    Console.SetOut(Console.Error);

    await McpServerHost.RunAsync(rootDir, solution, settings.LogLevel, cancellationToken);
    return 0;
  }

  private static string? FindSolution(string rootDir)
  {
    var solutions = Directory.EnumerateFiles(rootDir)
        .Where(DotnetFileTypes.IsAnySolutionFile)
        .ToList();

    return solutions.Count switch
    {
      0 => null,
      1 => solutions[0],
      _ => throw new InvalidOperationException(
          $"Several solutions in '{rootDir}': {string.Join(", ", solutions.Select(Path.GetFileName))}. Pick one with --solution."),
    };
  }
}