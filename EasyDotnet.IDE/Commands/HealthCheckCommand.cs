using System.Diagnostics;
using System.Text.Json;
using EasyDotnet.IDE.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class HealthCheckCommand : AsyncCommand<HealthCheckCommand.Settings>
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  public sealed class Settings : CommandSettings
  {
    [CommandOption("--format <FORMAT>")]
    public string Format { get; init; } = "json";

    public override ValidationResult Validate()
    {
      return Format is "json" or "markdown"
          ? ValidationResult.Success()
          : ValidationResult.Error("--format must be either json or markdown");
    }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var health = await GetHealthChecksAsync(cancellationToken);

    if (settings.Format == "markdown")
    {
      WriteMarkdown(health);
      return 0;
    }

    Console.WriteLine(JsonSerializer.Serialize(health, JsonOptions));
    return 0;
  }

  private static async Task<HealthCheckItem[]> GetHealthChecksAsync(CancellationToken cancellationToken)
  {
    var dotnet = await GetDotnetInfoAsync(cancellationToken);
    var roslyn = await RoslynToolService.GetStatusAsync(cancellationToken);

    List<HealthCheckItem> checks =
    [
      dotnet.Version is null
        ? HealthCheckItem.Error("dotnet.version", "dotnet was not found", [dotnet.Error ?? "Install the .NET SDK and ensure dotnet is on PATH"])
        : HealthCheckItem.Ok("dotnet.version", dotnet.Version),

      dotnet.Sdks.Length == 0
        ? HealthCheckItem.Warn("dotnet.sdks", "No .NET SDKs found", ["Install the .NET SDK"])
        : HealthCheckItem.Ok("dotnet.sdks", string.Join("; ", dotnet.Sdks))
    ];

    if (!roslyn.IsInstalled)
    {
      checks.Add(HealthCheckItem.Warn(
          "roslyn.tool",
          roslyn.Message ?? "roslyn-language-server is not installed",
          ["easy-dotnet will try to install it automatically when Roslyn LSP starts", "Manual install: dotnet-easydotnet roslyn install"]));
    }
    else if (roslyn.IsBelowRecommended)
    {
      checks.Add(HealthCheckItem.Warn(
          "roslyn.tool",
          $"roslyn-language-server {roslyn.Version} is below the recommended version {roslyn.MinimumRecommendedVersion}",
          ["Update using: dotnet-easydotnet roslyn update"]));
    }
    else
    {
      checks.Add(HealthCheckItem.Ok("roslyn.tool", $"roslyn-language-server {roslyn.Version}"));
    }

    return [.. checks];
  }

  private static void WriteMarkdown(IEnumerable<HealthCheckItem> checks)
  {
    foreach (var check in checks)
    {
      Console.WriteLine($"- {check.Type}: {check.Name} - {check.Value}");
      foreach (var advice in check.Advice)
      {
        Console.WriteLine($"  - {advice}");
      }
    }
  }

  private static async Task<DotnetHealthInfo> GetDotnetInfoAsync(CancellationToken cancellationToken)
  {
    var version = await RunProcessAsync("dotnet", ["--version"], cancellationToken);
    var sdks = await RunProcessAsync("dotnet", ["--list-sdks"], cancellationToken);

    return new DotnetHealthInfo(
        Version: version.ExitCode == 0 ? version.Output.Trim() : null,
        Sdks: sdks.ExitCode == 0 ? [.. sdks.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)] : [],
        Error: version.ExitCode == 0 ? null : version.Output);
  }

  private static async Task<CommandOutput> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
  {
    try
    {
      var startInfo = new ProcessStartInfo(fileName)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in arguments)
      {
        startInfo.ArgumentList.Add(argument);
      }

      using var process = new Process { StartInfo = startInfo };
      process.Start();

      var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
      var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);

      var output = string.Join(
          Environment.NewLine,
          new[] { await stdout, await stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));

      return new CommandOutput(process.ExitCode, output.Trim());
    }
    catch (Exception ex)
    {
      return new CommandOutput(1, ex.Message);
    }
  }

  private sealed record CommandOutput(int ExitCode, string Output);
  private sealed record DotnetHealthInfo(string? Version, string[] Sdks, string? Error);
  private sealed record HealthCheckItem(string Type, string Name, string Value, string[] Advice)
  {
    public static HealthCheckItem Ok(string name, string value) => new("ok", name, value, []);
    public static HealthCheckItem Warn(string name, string value, string[] advice) => new("warn", name, value, advice);
    public static HealthCheckItem Error(string name, string value, string[] advice) => new("error", name, value, advice);
  }
}
