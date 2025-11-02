using System.Reflection;

namespace EasyDotnet.Infrastructure.Services;

public static class RoslynLocator
{
  private static readonly string RzlsPath = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "nvim-data",
          "mason",
          "packages",
          "rzls",
          "libexec");

  private static readonly string RoslynPath = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "nvim-data",
          "mason",
          "packages",
          "roslyn"
 );


  /// <summary>
  /// Returns the full path to the Roslyn LSP DLL inside the .NET tool installation folder.
  /// </summary>
  public static string GetRoslynDllPath()
  {
    var roslynDir = GetRoslynBaseDir();
    var roslynDll = Path.Combine(RoslynPath, "roslyn.cmd");

    if (!File.Exists(roslynDll))
    {
      throw new FileNotFoundException("Roslyn LSP DLL not found", roslynDll);
    }

    return roslynDll;
  }

  public static string GetRazorDllPath()
  {
    var roslynDir = GetRoslynBaseDir();
    // C:\Users\Gustav\AppData\Local\nvim-data\mason\packages\rzls\libexec
    var razorDll = Path.Combine(RzlsPath, "Microsoft.CodeAnalysis.Razor.Compiler.dll");

    if (!File.Exists(razorDll))
    {
      throw new FileNotFoundException("Razor LSP DLL not found", razorDll);
    }

    return razorDll;
  }

  public static string GetRazorExtensionDllPath()
  {
    var roslynDir = GetRoslynBaseDir();

    // C:\Users\Gustav\AppData\Local\nvim-data\mason\packages\rzls\libexec\RazorExtension
    var razorExtensionDll = Path.Combine(RzlsPath, "RazorExtension", "Microsoft.VisualStudioCode.RazorExtension.dll");

    if (!File.Exists(razorExtensionDll))
    {
      throw new FileNotFoundException("Razor extension DLL not found", razorExtensionDll);
    }

    return razorExtensionDll;
  }

  public static string GetRazorTargetsPath()
  {
    // C:\Users\Gustav\AppData\Local\nvim-data\mason\packages\rzls\libexec\Targets
    var roslynDir = GetRoslynBaseDir();
    var razorTargets = Path.Combine(RzlsPath, "Targets", "Microsoft.NET.Sdk.Razor.DesignTime.targets");

    if (!File.Exists(razorTargets))
    {
      throw new FileNotFoundException("Razor targets file not found", razorTargets);
    }

    return razorTargets;
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