using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EasyDotnet.IDE;

public static class CompatCommandHandler
{
  public static bool IsCompatRunCommand(string[] args) =>
      args.Length >= 2 &&
      args[0].Equals("compat", StringComparison.OrdinalIgnoreCase) &&
      args[1].Equals("run", StringComparison.OrdinalIgnoreCase);

  public static async Task<int> HandleAsync(string[] args)
  {
    // Example usage:
    // easy-dotnet compat run <project.csproj>
    //   --msbuild "C:\Path\To\MSBuild.exe"
    //   --target "C:\Path\To\App.exe"
    //   [--with-iis]
    //   [--iis-exe "C:\Path\To\IISExpress.exe"]
    //   [--config "C:\Path\To\applicationhost.config"]
    //   [--site "MyApp"]

    var projectPath = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    if (string.IsNullOrEmpty(projectPath))
    {
      Console.Error.WriteLine("Usage: easy-dotnet compat run <project.csproj> --msbuild <path> --target <path> [--with-iis] [--iis-exe <path>] [--config <path>] [--site <name>]");
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

    var buildExit = await RunProcessAsync(msbuildPath, $"\"{projectPath}\" /restore /t:Build /nologo");
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

  private static string? GetArgValue(string[] args, string name)
  {
    var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
  }

  private static async Task<int> RunProcessAsync(string fileName, string arguments)
  {
    using var process = new Process
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
}