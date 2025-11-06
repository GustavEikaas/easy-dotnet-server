using System.Diagnostics;
using EasyDotnet.MsBuild;
using Spectre.Console;

namespace EasyDotnet.Infrastructure.Framework;

public static class CompatCommandHandler
{
  public static bool IsCompatCommand(string[] args) =>
        args.Length >= 2 &&
        args[0].Equals("compat", StringComparison.OrdinalIgnoreCase);
  private const string Shim = "dotnet easydotnet compat";

  public static string GetBuildCommand(string projectPath, string msbuildPath) => $"{Shim} build \"{projectPath}\" --msbuild \"{msbuildPath}\"";
  public static string GetTestCommand(string projectPath, string targetPath, string msbuildPath, string vstestPath) => $"{Shim} test \"{projectPath}\" --target \"{targetPath}\" --msbuild \"{msbuildPath}\" --vstest \"{vstestPath}\"";
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
    var buildExit = await RunBuildWithSpectreAsync(msbuildPath, projectPath);
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
    var projectPath = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    var targetPath = GetArgValue(args, "--target");
    var vstestPath = GetArgValue(args, "--vstest");
    var msbuildPath = GetArgValue(args, "--msbuild");

    if (string.IsNullOrEmpty(projectPath))
    {
      Console.Error.WriteLine("Usage: dotnet easydotnet compat test <project.csproj> --msbuild <path> --target <path> --vstest <path>");
      return 1;
    }

    if (string.IsNullOrEmpty(msbuildPath) || !File.Exists(msbuildPath))
    {
      Console.Error.WriteLine("[compat] Missing or invalid --msbuild path.");
      return 1;
    }

    if (string.IsNullOrEmpty(vstestPath) || !File.Exists(vstestPath))
    {
      Console.Error.WriteLine("[compat] Missing or invalid --vstest path.");
      return 1;
    }

    if (string.IsNullOrEmpty(targetPath))
    {
      Console.Error.WriteLine("[compat] Missing required --target argument (expected test assembly path).");
      return 1;
    }

    Console.WriteLine("[compat] Building project before test...");
    var buildExit = await RunBuildWithSpectreAsync(msbuildPath, projectPath);
    if (buildExit != 0)
    {
      Console.Error.WriteLine($"[compat] Build failed (exit code {buildExit}). Aborting test run.");
      return buildExit;
    }

    Console.WriteLine($"[compat] Running tests from {targetPath}...");
    var testExit = await RunProcessAsync("dotnet", $"\"{vstestPath}\" \"{targetPath}\"");

    if (testExit == 0)
      Console.WriteLine("[compat] Tests completed successfully.");
    else
      Console.Error.WriteLine($"[compat] Tests failed (exit code {testExit}).");

    return testExit;
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
    return await RunBuildWithSpectreAsync(msbuildPath, projectPath);
  }

  private static int UnknownSubcommand(string cmd)
  {
    Console.Error.WriteLine($"Unknown compat command: {cmd}");
    Console.Error.WriteLine("Supported commands: run, build, test");
    return 1;
  }

  private static string? GetArgValue(string[] args, string name) =>
      args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase))
          .Skip(1)
          .FirstOrDefault();

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

  public static async Task<int> RunBuildWithSpectreAsync(string msbuildPath, string projectPath)
  {
    if (!File.Exists(msbuildPath))
      throw new FileNotFoundException("MSBuild not found.", msbuildPath);

    if (!File.Exists(projectPath))
      throw new FileNotFoundException("Project file not found.", projectPath);

    var exitCode = 0;

    // Status spinner while building
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[yellow]Building project...[/]", async ctx =>
        {
          var process = new System.Diagnostics.Process
          {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
              FileName = msbuildPath,
              Arguments = $"\"{projectPath}\" /nologo /m /verbosity:minimal",
              RedirectStandardOutput = true,
              RedirectStandardError = true,
              UseShellExecute = false,
              CreateNoWindow = true
            }
          };

          process.OutputDataReceived += (_, e) =>
          {
            if (string.IsNullOrEmpty(e.Data))
              return;

            var msg = MsBuildBuildStdoutParser.ParseMsBuildLines(e.Data).FirstOrDefault();

            if (msg != null)
            {
              if (msg.Type.Equals("error", StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[red]{msg.Message.EscapeMarkup()}[/]");
              else if (msg.Type.Equals("warning", StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[yellow]{msg.Message.EscapeMarkup()}[/]");
              else
                AnsiConsole.MarkupLine($"[grey]{e.Data.EscapeMarkup()}[/]");
            }
            else
            {
              AnsiConsole.MarkupLine($"[grey]{e.Data.EscapeMarkup()}[/]");
            }
          };

          process.ErrorDataReceived += (_, e) => { if (e.Data != null) AnsiConsole.MarkupLine($"[red]{e.Data.EscapeMarkup()}[/]"); };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
          await process.WaitForExitAsync();

          exitCode = process.ExitCode;
        });

    // Success / Failure message
    if (exitCode == 0)
      AnsiConsole.MarkupLine("[bold green]✔ Build succeeded![/]");
    else
      AnsiConsole.MarkupLine("[bold red]✖ Build failed![/]");

    return exitCode;
  }
}