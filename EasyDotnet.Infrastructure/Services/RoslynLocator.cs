using System.Reflection;

namespace EasyDotnet.Infrastructure.Services;

public static class RoslynLocator
{
  public const string ROSLYN_DLL_PATH_ENV = "EASY_DOTNET_ROSLYN_DLL_PATH";

  /// <summary>
  /// Returns the full path to the Roslyn LSP DLL.
  /// Checks EASY_DOTNET_ROSLYN_DLL_PATH first, then falls back to bundled version.
  /// </summary>
  public static string GetRoslynDllPath()
  {
    var customDllPath = Environment.GetEnvironmentVariable(ROSLYN_DLL_PATH_ENV);
    if (!string.IsNullOrWhiteSpace(customDllPath))
    {
      if (File.Exists(customDllPath))
      {
        return customDllPath;
      }
      throw new FileNotFoundException(
        $"Custom Roslyn DLL specified in {ROSLYN_DLL_PATH_ENV} not found",
        customDllPath);
    }


    var roslynDir = GetRoslynBaseDir();
    var roslynDll = Path.Combine(roslynDir, "LanguageServer", "Microsoft.CodeAnalysis.LanguageServer.dll");

    if (!File.Exists(roslynDll))
    {
      throw new FileNotFoundException("Roslyn LSP DLL not found", roslynDll);
    }

    return roslynDll;
  }

  /// <summary>
  /// Returns the full paths of the Roslynator C# analyzer DLLs inside the .NET tool installation folder.
  /// Only the DLLs containing analyzers are returned.
  /// </summary>
  public static string[] GetRoslynatorAnalyzers()
  {
    var analyzersDir = Path.Combine(GetRoslynBaseDir(), "Analyzers");

    if (!Directory.Exists(analyzersDir))
    {
      throw new DirectoryNotFoundException($"Roslynator Analyzers folder not found: {analyzersDir}");
    }

    var analyzerDlls = new[]
    {
            "Roslynator.CSharp.Analyzers.dll",
            "Roslynator.CSharp.Analyzers.CodeFixes.dll"
    };

    return [.. analyzerDlls
        .Select(dll => Path.Combine(analyzersDir, dll))
        .Where(File.Exists)];
  }

  /// <summary>
  /// Returns the base Roslyn folder inside the .NET tool installation folder.
  /// </summary>
  private static string GetRoslynBaseDir()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");

    var storeToolsDir = Path.Combine(toolExeDir, "..", "..", "..", "tools");
    return Path.GetFullPath(Path.Combine(storeToolsDir, "Roslyn"));
  }
}