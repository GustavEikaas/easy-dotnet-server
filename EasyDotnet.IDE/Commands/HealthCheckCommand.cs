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

    public override ValidationResult Validate()
    {
      return Format is "json" or "markdown"
          ? ValidationResult.Success()
          : ValidationResult.Error("--format must be either json or markdown");
    }
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    var health = await GetHealthChecksAsync(settings.DebuggerBinPath, cancellationToken);

    if (settings.Format == "markdown")
    {
      WriteMarkdown(health);
      return 0;
    }

    Console.WriteLine(JsonSerializer.Serialize(health, JsonOptions));
    return 0;
  }

  private static async Task<HealthCheckItem[]> GetHealthChecksAsync(string? debuggerBinPath, CancellationToken cancellationToken)
  {
    var dotnet = await GetDotnetInfoAsync(cancellationToken);
    var roslyn = await RoslynToolService.GetStatusAsync(cancellationToken);
    var netcoredbg = await GetNetCoreDbgInfoAsync(debuggerBinPath, cancellationToken);

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

    checks.AddRange(GetNetCoreDbgHealthChecks(netcoredbg));

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

  private static async Task<NetCoreDbgHealthInfo> GetNetCoreDbgInfoAsync(string? debuggerBinPath, CancellationToken cancellationToken)
  {
    var platform = GetNetCoreDbgPlatform();
    var envPath = Environment.GetEnvironmentVariable(NetCoreDbgLocator.DEBUGGER_PATH_ENV);
    var customPath = !string.IsNullOrWhiteSpace(debuggerBinPath) ? debuggerBinPath : envPath;
    var source = string.IsNullOrWhiteSpace(customPath)
        ? "bundled"
        : !string.IsNullOrWhiteSpace(debuggerBinPath)
            ? "--debugger-bin-path"
            : $"{NetCoreDbgLocator.DEBUGGER_PATH_ENV}";

    try
    {
      var path = !string.IsNullOrWhiteSpace(customPath)
          ? GetCustomNetCoreDbgPath(customPath)
          : NetCoreDbgLocator.GetNetCoreDbgPath();
      var version = await RunProcessAsync(path, ["--version"], cancellationToken);

      return new NetCoreDbgHealthInfo(
          Source: source,
          Platform: platform,
          Path: path,
          Version: version.ExitCode == 0 ? version.Output : null,
          Error: version.ExitCode == 0 ? null : version.Output);
    }
    catch (Exception ex)
    {
      return new NetCoreDbgHealthInfo(
          Source: source,
          Platform: platform,
          Path: GetExpectedNetCoreDbgPath(customPath, platform),
          Version: null,
          Error: ex.Message);
    }
  }

  private static string GetCustomNetCoreDbgPath(string path)
  {
    if (File.Exists(path))
    {
      return path;
    }

    throw new FileNotFoundException("Custom netcoredbg executable not found", path);
  }

  private static IEnumerable<HealthCheckItem> GetNetCoreDbgHealthChecks(NetCoreDbgHealthInfo netcoredbg)
  {
    yield return HealthCheckItem.Ok("debugger.netcoredbg.source", netcoredbg.Source);
    yield return netcoredbg.Platform is null
        ? HealthCheckItem.Warn("debugger.netcoredbg.platform", "Unable to determine netcoredbg platform", [netcoredbg.Error ?? "Unsupported OS or architecture"])
        : HealthCheckItem.Ok("debugger.netcoredbg.platform", netcoredbg.Platform);

    if (string.IsNullOrWhiteSpace(netcoredbg.Path))
    {
      yield return HealthCheckItem.Warn(
          "debugger.netcoredbg.path",
          "netcoredbg was not found",
          [netcoredbg.Error ?? "Install easy-dotnet with bundled assets or set EASY_DOTNET_DEBUGGER_BIN_PATH"]);
      yield break;
    }

    if (!File.Exists(netcoredbg.Path))
    {
      yield return HealthCheckItem.Warn(
          "debugger.netcoredbg.path",
          netcoredbg.Path,
          [netcoredbg.Error ?? "The resolved netcoredbg path does not exist"]);
      yield break;
    }

    yield return HealthCheckItem.Ok("debugger.netcoredbg.path", netcoredbg.Path);

    yield return netcoredbg.Version is null
        ? HealthCheckItem.Warn("debugger.netcoredbg.version", "Unable to read netcoredbg --version", [netcoredbg.Error ?? "The command exited without version output"])
        : HealthCheckItem.Ok("debugger.netcoredbg.version", netcoredbg.Version);
  }

  private static string? GetNetCoreDbgPlatform()
  {
    try
    {
      return NetCoreDbgLocator.GetRuntimePlatform();
    }
    catch
    {
      return null;
    }
  }

  private static string? GetExpectedNetCoreDbgPath(string? customPath, string? platform)
  {
    if (!string.IsNullOrWhiteSpace(customPath))
    {
      return customPath;
    }

    return platform is null
        ? null
        : NetCoreDbgLocator.GetBundledNetCoreDbgPath(platform);
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
  private sealed record NetCoreDbgHealthInfo(string Source, string? Platform, string? Path, string? Version, string? Error);
  private sealed record HealthCheckItem(string Type, string Name, string Value, string[] Advice)
  {
    public static HealthCheckItem Ok(string name, string value) => new("ok", name, value, []);
    public static HealthCheckItem Warn(string name, string value, string[] advice) => new("warn", name, value, advice);
    public static HealthCheckItem Error(string name, string value, string[] advice) => new("error", name, value, advice);
  }
}
