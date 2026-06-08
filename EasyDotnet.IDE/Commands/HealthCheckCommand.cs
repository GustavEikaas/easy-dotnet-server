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

    [CommandOption("--debugger-bin-path <PATH>")]
    public string? DebuggerBinPath { get; init; }

    [CommandOption("--debugger-engine <ENGINE>")]
    public string? DebuggerEngine { get; init; }

    public override ValidationResult Validate()
    {
      if (Format is not ("json" or "markdown"))
      {
        return ValidationResult.Error("--format must be either json or markdown");
      }

      if (!string.IsNullOrWhiteSpace(DebuggerEngine))
      {
        try
        {
          DebuggerLocator.ParseEngine(DebuggerEngine);
        }
        catch (ArgumentException ex)
        {
          return ValidationResult.Error(ex.Message);
        }
      }

      return ValidationResult.Success();
    }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var health = await GetHealthChecksAsync(settings.DebuggerBinPath, settings.DebuggerEngine, cancellationToken);

    if (settings.Format == "markdown")
    {
      WriteMarkdown(health);
      return 0;
    }

    Console.WriteLine(JsonSerializer.Serialize(health, JsonOptions));
    return 0;
  }

  private static async Task<HealthCheckItem[]> GetHealthChecksAsync(string? debuggerBinPath, string? debuggerEngine, CancellationToken cancellationToken)
  {
    var dotnet = await GetDotnetInfoAsync(cancellationToken);
    var roslyn = await RoslynToolService.GetStatusAsync(cancellationToken);
    var debugger = await GetDebuggerInfoAsync(debuggerBinPath, debuggerEngine, cancellationToken);

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

    checks.AddRange(GetDebuggerHealthChecks(debugger));

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

  private static async Task<DebuggerHealthInfo> GetDebuggerInfoAsync(string? debuggerBinPath, string? debuggerEngine, CancellationToken cancellationToken)
  {
    var platform = DebuggerLocator.TryGetRuntimePlatform();
    var envPath = Environment.GetEnvironmentVariable(DebuggerLocator.DEBUGGER_PATH_ENV);
    var customPath = !string.IsNullOrWhiteSpace(debuggerBinPath) ? debuggerBinPath : envPath;

    try
    {
      var resolved = DebuggerLocator.ResolveDebugger(debuggerEngine, debuggerBinPath);
      var version = await RunProcessAsync(resolved.Path, ["--version"], cancellationToken);

      return new DebuggerHealthInfo(
          Engine: DebuggerLocator.GetEngineName(resolved.Engine),
          Source: resolved.Source,
          Platform: resolved.Platform,
          Path: resolved.Path,
          Version: version.ExitCode == 0 ? version.Output : null,
          Error: version.ExitCode == 0 ? null : version.Output);
    }
    catch (Exception ex)
    {
      var engine = TryGetDebuggerEngineName(debuggerEngine);
      return new DebuggerHealthInfo(
          Engine: engine,
          Source: GetDebuggerSource(debuggerBinPath, customPath),
          Platform: platform,
          Path: GetExpectedDebuggerPath(customPath, platform, engine),
          Version: null,
          Error: ex.Message);
    }
  }

  private static IEnumerable<HealthCheckItem> GetDebuggerHealthChecks(DebuggerHealthInfo debugger)
  {
    yield return HealthCheckItem.Ok("debugger.engine", debugger.Engine);
    yield return HealthCheckItem.Ok("debugger.source", debugger.Source);
    yield return debugger.Platform is null
        ? HealthCheckItem.Warn("debugger.platform", "Unable to determine debugger platform", [debugger.Error ?? "Unsupported OS or architecture"])
        : HealthCheckItem.Ok("debugger.platform", debugger.Platform);

    if (string.IsNullOrWhiteSpace(debugger.Path))
    {
      yield return HealthCheckItem.Warn(
          "debugger.path",
          "Debugger was not found",
          [debugger.Error ?? "Install easy-dotnet with bundled assets or set EASY_DOTNET_DEBUGGER_BIN_PATH"]);
      yield break;
    }

    if (!File.Exists(debugger.Path))
    {
      yield return HealthCheckItem.Warn(
          "debugger.path",
          debugger.Path,
          [debugger.Error ?? "The resolved debugger path does not exist"]);
      yield break;
    }

    yield return HealthCheckItem.Ok("debugger.path", debugger.Path);

    yield return debugger.Version is null
        ? HealthCheckItem.Warn("debugger.version", $"Unable to read {debugger.Engine} --version", [debugger.Error ?? "The command exited without version output"])
        : HealthCheckItem.Ok("debugger.version", debugger.Version);
  }

  private static string TryGetDebuggerEngineName(string? debuggerEngine)
  {
    try
    {
      return DebuggerLocator.GetEngineName(DebuggerLocator.GetConfiguredEngine(debuggerEngine));
    }
    catch (ArgumentException)
    {
      return !string.IsNullOrWhiteSpace(debuggerEngine)
          ? debuggerEngine
          : Environment.GetEnvironmentVariable(DebuggerLocator.DEBUGGER_ENGINE_ENV) ?? "unknown";
    }
  }

  private static string GetDebuggerSource(string? debuggerBinPath, string? customPath) =>
    string.IsNullOrWhiteSpace(customPath)
        ? "bundled"
        : !string.IsNullOrWhiteSpace(debuggerBinPath)
            ? "--debugger-bin-path"
            : DebuggerLocator.DEBUGGER_PATH_ENV;

  private static string? GetExpectedDebuggerPath(string? customPath, string? platform, string engine)
  {
    if (!string.IsNullOrWhiteSpace(customPath))
    {
      return customPath;
    }

    if (platform is null)
    {
      return null;
    }

    try
    {
      return DebuggerLocator.GetBundledDebuggerPath(DebuggerLocator.ParseEngine(engine), platform);
    }
    catch (ArgumentException)
    {
      return null;
    }
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
  private sealed record DebuggerHealthInfo(string Engine, string Source, string? Platform, string? Path, string? Version, string? Error);
  private sealed record HealthCheckItem(string Type, string Name, string Value, string[] Advice)
  {
    public static HealthCheckItem Ok(string name, string value) => new("ok", name, value, []);
    public static HealthCheckItem Warn(string name, string value, string[] advice) => new("warn", name, value, advice);
    public static HealthCheckItem Error(string name, string value, string[] advice) => new("error", name, value, advice);
  }
}