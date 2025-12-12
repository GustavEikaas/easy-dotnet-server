using System.Reflection;

namespace EasyDotnet.ProjXLanguageServer.Utils;

public static class AssemblyScanner
{
  public static List<Type> GetControllerTypes() => [.. new List<Assembly>() { Assembly.GetExecutingAssembly() }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseController).IsAssignableFrom(t))];
}