using System.Reflection;

namespace EasyDotnet.Infrastructure.Services;

public static class RoslynLocator
{
  /// <summary>
  /// Returns the full path to the Roslyn LSP DLL inside the .NET tool installation folder.
  /// </summary>
  public static string GetRoslynDllPath()
  {
    // 1. Get the directory where the tool DLL itself is running
    var assemblyLocation = Assembly.GetExecutingAssembly().Location;
    Console.WriteLine($"[DEBUG] Assembly location: {assemblyLocation}");

    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");
    Console.WriteLine($"[DEBUG] Tool executable directory: {toolExeDir}");

    // 2. Navigate up to the store folder structure
    var storeToolsDir = Path.Combine(toolExeDir, "..", "..", "..", "tools");
    var normalizedDir = Path.GetFullPath(storeToolsDir);
    Console.WriteLine($"[DEBUG] Calculated store tools directory: {normalizedDir}");

    // 3. Combine with the Roslyn DLL name
    var roslynDll = Path.Combine(normalizedDir, "Microsoft.CodeAnalysis.LanguageServer.dll");
    Console.WriteLine($"[DEBUG] Calculated Roslyn DLL path: {roslynDll}");

    if (!File.Exists(roslynDll))
    {
      Console.WriteLine("[WARN] Roslyn DLL not found at expected path.");
      throw new FileNotFoundException("Roslyn LSP DLL not found", roslynDll);
    }

    Console.WriteLine("[DEBUG] Roslyn DLL found!");
    return roslynDll;
  }
}
