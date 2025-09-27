using System.Reflection;

namespace EasyDotnet.Infrastructure.Services;

public static class RoslynLocator
{
  /// <summary>
  /// Returns the full path to the Roslyn LSP DLL inside the .NET tool installation folder.
  /// </summary>
  public static string GetRoslynDllPath()
  {
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Unable to determine assembly directory");

    var storeToolsDir = Path.Combine(toolExeDir, "..", "..", "..", "tools");
    var normalizedDir = Path.GetFullPath(storeToolsDir);
    var roslynDll = Path.Combine(normalizedDir, "Microsoft.CodeAnalysis.LanguageServer.dll");

    if (!File.Exists(roslynDll))
    {
      throw new FileNotFoundException("Roslyn LSP DLL not found", roslynDll);
    }

    return roslynDll;
  }
}