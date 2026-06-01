using System.Reflection;

namespace EasyDotnet.IDE.Services;

public static class RoslynLocator
{
  public const string ROSLYN_DLL_PATH_ENV = "EASY_DOTNET_ROSLYN_DLL_PATH";
  private const string EasyDotnetRoslynLanguageServicesFileName = "EasyDotnet.RoslynLanguageServices.dll";
  private const string ExternalAccessExtensionsFileName = "Microsoft.CodeAnalysis.ExternalAccess.Extensions.dll";

  public static string? GetCustomRoslynDllPath()
  {
    var customDllPath = Environment.GetEnvironmentVariable(ROSLYN_DLL_PATH_ENV);
    if (string.IsNullOrWhiteSpace(customDllPath))
    {
      return null;
    }

    if (File.Exists(customDllPath))
    {
      return customDllPath;
    }

    throw new FileNotFoundException(
        $"Custom Roslyn DLL specified in {ROSLYN_DLL_PATH_ENV} not found",
        customDllPath);
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
  /// Returns the full paths of the Roslynator C# analyzer DLLs inside the .NET tool installation folder.
  /// Only the DLLs containing analyzers are returned.
  /// </summary>
  public static string[] GetEasyDotnetAnalyzers()
  {
    var analyzersDir = Path.Combine(GetRoslynBaseDir(), "Analyzers");

    if (!Directory.Exists(analyzersDir))
    {
      throw new DirectoryNotFoundException($"EasyDotnet Analyzer folder not found: {analyzersDir}");
    }

    var analyzerDlls = new[] { EasyDotnetRoslynLanguageServicesFileName };

    return [.. analyzerDlls
        .Select(dll => Path.Combine(analyzersDir, dll))
        .Where(File.Exists)];
  }

  public static string GetEasyDotnetRoslynLanguageServicesPath()
  {
    var analyzerPath = GetEasyDotnetAnalyzers().SingleOrDefault();
    if (string.IsNullOrWhiteSpace(analyzerPath))
    {
      throw new FileNotFoundException(
          "EasyDotnet.RoslynLanguageServices.dll was not found in the bundled Roslyn analyzer directory.",
          Path.Combine(GetRoslynBaseDir(), "Analyzers", EasyDotnetRoslynLanguageServicesFileName));
    }

    return analyzerPath;
  }

  public static string? GetExternalAccessExtensionsPath()
  {
    var path = Path.Combine(GetRoslynBaseDir(), "DevKit", ExternalAccessExtensionsFileName);
    return File.Exists(path) ? path : null;
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