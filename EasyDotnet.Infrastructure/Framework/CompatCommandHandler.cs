using System.Diagnostics;

namespace EasyDotnet.Infrastructure.Framework;

public static class CompatCommandHandler
{
  public static bool IsCompatCommand(string[] args) =>
        args.Length >= 2 &&
        args[0].Equals("compat", StringComparison.OrdinalIgnoreCase);
  private const string Shim = "dotnet easydotnet compat";

  public static string GetBuildCommand(string projectPath, string msbuildPath) => $"{Shim} build \"{projectPath}\" --msbuild \"{msbuildPath}\"";
  public static string GetTestCommand(string targetPath, string vstestPath) => $"{Shim} test \"{targetPath}\" --vstest \"{vstestPath}\"";
  public static string GetIisCommand(
        string projectPath,
        string msbuildPath,
        string targetPath,
        string iisExe,
        string configPath,
        string siteName) =>
$"{Shim} run \"{projectPath}\" --msbuild \"{msbuildPath}\" --target \"{targetPath}\" --with-iis --iis-exe \"{iisExe}\" --config \"{configPath}\" --site \"{siteName}\"";

  public static string GetRunCommand(
        string projectPath,
        string msbuildPath,
        string targetPath) => $"{Shim} run \"{projectPath}\" --msbuild \"{msbuildPath}\" --target \"{targetPath}\"";

  public static async Task<int> HandleAsync(string[] args)
  {
    if (args.Length < 2)
    {
      Console.Error.WriteLine("Usage: dotnet easydotnet compat <run|build|test> ...");
      return 1;
    }
    var subCommand = args[1].ToLowerInvariant();

    return subCommand switch
    {
      "run" => await HandleRunAsync(args),
      "build" => await HandleBuildAsync(args),
      "test" => await HandleTestAsync(args),
      _ => UnknownSubcommand(subCommand)
    };
  }
  private static async Task<int> HandleRunAsync(string[] args)
  {
    var projectPath = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    if (string.IsNullOrEmpty(projectPath))
    {
      Console.Error.WriteLine("Usage: dotnet easydotnet compat run <project.csproj> --msbuild <path> --target <path> [--with-iis] [--iis-exe <path>] [--config <path>] [--site <name>]");
      return 1;
    }

    var msbuildPath = GetArgValue(args, "--msbuild");
    var targetPath = GetArgValue(args, "--target");
    var withIis = args.Contains("--with-iis", StringComparer.OrdinalIgnoreCase);
    var siteName = GetArgValue(args, "--site");
    var iisExe = GetArgValue(args, "--iis-exe");
    var configPath = GetArgValue(args, "--config");

    if (string.IsNullOrEmpty(msbuildPath) || !File.Exists(msbuildPath))
    {
      Console.Error.WriteLine("[compat] Missing or invalid --msbuild path.");
      return 1;
    }

    if (string.IsNullOrEmpty(targetPath))
    {
      Console.Error.WriteLine("[compat] Missing required --target argument.");
      return 1;
    }

    Console.WriteLine($"[compat] Project: {projectPath}");
    Console.WriteLine($"[compat] MSBuild: {msbuildPath}");
    Console.WriteLine($"[compat] Target: {targetPath}");

    var buildExit = await RunProcessWithSpinnerAsync(msbuildPath, $"\"{projectPath}\" /nologo /m /verbosity:minimal", "[compat] Building...");
    if (buildExit != 0)
    {
      Console.Error.WriteLine($"[compat] Build failed (exit code {buildExit}). Aborting run.");
      return buildExit;
    }

    if (withIis)
    {
      if (string.IsNullOrEmpty(iisExe) || !File.Exists(iisExe))
      {
        Console.Error.WriteLine("[compat] Missing or invalid --iis-exe path (required when using --with-iis).");
        return 1;
      }

      if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
      {
        Console.Error.WriteLine("[compat] Missing or invalid --config path (required when using --with-iis).");
        return 1;
      }

      var argsLine = $"/config:\"{configPath}\" /site:\"{siteName ?? Path.GetFileNameWithoutExtension(projectPath)}\"";
      return await RunProcessAsync(iisExe, argsLine);
    }
    else
    {
      if (!File.Exists(targetPath))
      {
        Console.Error.WriteLine($"[compat] Target executable not found: {targetPath}");
        return 1;
      }

      return await RunProcessAsync(targetPath, "");
    }
  }

  private static async Task<int> HandleTestAsync(string[] args)
  {
    // Example:
    // dotnet easydotnet compat test <target.dll> [--vstest "C:\Path\To\vstest.console.exe"]
    var targetPath = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    var vstestPath = GetArgValue(args, "--vstest");

    if (string.IsNullOrEmpty(targetPath))
    {
      Console.Error.WriteLine("Usage: dotnet easydotnet compat test <target.dll> [--vstest <path>]");
      return 1;
    }

    if (!File.Exists(targetPath))
    {
      //dll not found
      Console.Error.WriteLine($"[compat] Target not found: {targetPath}");
      return 1;
    }

    if (string.IsNullOrEmpty(vstestPath))
    {
      Console.Error.WriteLine($"[compat] vstest not found: {vstestPath}");
      return 1;
    }

    Console.WriteLine($"[compat] Running tests with: {vstestPath}");
    Console.WriteLine($"[compat] Target: {targetPath}");

    return await RunProcessAsync("dotnet", $"\"{vstestPath}\" \"{targetPath}\"");
  }

  private static async Task<int> HandleBuildAsync(string[] args)
  {
    // Example:
    // dotnet easydotnet compat build <project.csproj> --msbuild "C:\Path\To\MSBuild.exe"

    var projectPath = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    var msbuildPath = GetArgValue(args, "--msbuild");

    if (string.IsNullOrEmpty(projectPath))
    {
      Console.Error.WriteLine("Usage: dotnet easydotnet compat build <project.csproj> --msbuild <path>");
      return 1;
    }

    if (string.IsNullOrEmpty(msbuildPath) || !File.Exists(msbuildPath))
    {
      Console.Error.WriteLine("[compat] Missing or invalid --msbuild path.");
      return 1;
    }

    Console.WriteLine($"[compat] Building project: {projectPath}");
    Console.WriteLine($"[compat] Using MSBuild: {msbuildPath}");

    return await RunProcessWithSpinnerAsync(msbuildPath, $"\"{projectPath}\" /nologo /m /verbosity:minimal", "[compat] Building...");
  }

  private static int UnknownSubcommand(string cmd)
  {
    Console.Error.WriteLine($"Unknown compat command: {cmd}");
    Console.Error.WriteLine("Supported commands: run, build, test");
    return 1;
  }

  private static string? GetArgValue(string[] args, string name)
  {
    var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
  }

  private static async Task<int> RunProcessAsync(string fileName, string arguments)
  {
    using var process = new System.Diagnostics.Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = false
      }
    };

    process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    await process.WaitForExitAsync();

    return process.ExitCode;
  }

  private static async Task<int> RunProcessWithSpinnerAsync(string fileName, string arguments, string statusMessage)
  {
    var spinnerFrames = new[] { "🛠️ ", "🔨", "⏳" };
    var spinnerIndex = 0;

    using var process = new System.Diagnostics.Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      }
    };

    var outputLines = new List<string>();
    var errorLines = new List<string>();

    process.OutputDataReceived += (_, e) => { if (e.Data != null) outputLines.Add(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorLines.Add(e.Data); };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    while (!process.HasExited)
    {
      Console.Write($"\r{statusMessage} {spinnerFrames[spinnerIndex % spinnerFrames.Length]}");
      spinnerIndex++;
      await Task.Delay(100);
    }

    await process.WaitForExitAsync();
    Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r");

    if (process.ExitCode != 0)
    {
      foreach (var line in outputLines) Console.WriteLine(line);
      foreach (var line in errorLines) Console.Error.WriteLine(line);
    }

    return process.ExitCode;
  }
}