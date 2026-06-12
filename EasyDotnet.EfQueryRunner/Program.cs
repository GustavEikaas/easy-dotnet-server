using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using EasyDotnet.EfQueryRunner;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

var arguments = ParseArgs(args);

try
{
  var targetAssemblyPath = Path.GetFullPath(arguments["--target-assembly"]);
  var startupAssemblyPath = arguments.TryGetValue("--startup-assembly", out var startupArg) ? Path.GetFullPath(startupArg) : null;
  var contextTypeName = arguments["--context-type"];
  var queryText = Encoding.UTF8.GetString(Convert.FromBase64String(arguments["--query-b64"]));
  var locals = arguments.TryGetValue("--locals-b64", out var localsB64)
    ? JsonSerializer.Deserialize<List<LocalVariable>>(Encoding.UTF8.GetString(Convert.FromBase64String(localsB64)))!
    : [];
  var usings = arguments.TryGetValue("--usings-b64", out var usingsB64)
    ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(Convert.FromBase64String(usingsB64)))!
    : [];

  // Probe the startup project's output first (its assembly versions reflect the running app),
  // then the target project's own output (which holds design-time-only deps such as
  // Microsoft.EntityFrameworkCore.Design when referenced with PrivateAssets="all").
  var probeDirs = new List<string>();
  if (startupAssemblyPath is not null)
  {
    probeDirs.Add(Path.GetDirectoryName(startupAssemblyPath)!);
  }
  var targetDir = Path.GetDirectoryName(targetAssemblyPath)!;
  if (!probeDirs.Contains(targetDir, StringComparer.OrdinalIgnoreCase))
  {
    probeDirs.Add(targetDir);
  }

  var resolvers = new[] { startupAssemblyPath, targetAssemblyPath }
    .OfType<string>()
    .Select(x =>
    {
      try
      {
        return new AssemblyDependencyResolver(x);
      }
      catch
      {
        return null;
      }
    })
    .OfType<AssemblyDependencyResolver>()
    .ToList();

  AssemblyLoadContext.Default.Resolving += (context, name) =>
  {
    var resolved = resolvers.Select(x => x.ResolveAssemblyToPath(name)).FirstOrDefault(x => x is not null);
    if (resolved is not null)
    {
      return context.LoadFromAssemblyPath(resolved);
    }
    var candidate = probeDirs.Select(x => Path.Combine(x, $"{name.Name}.dll")).FirstOrDefault(File.Exists);
    return candidate is not null ? context.LoadFromAssemblyPath(candidate) : null;
  };

  AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
  {
    var resolved = resolvers.Select(x => x.ResolveUnmanagedDllToPath(name)).FirstOrDefault(x => x is not null);
    return resolved is not null ? NativeLibrary.Load(resolved) : IntPtr.Zero;
  };

  var userAssemblies = LoadUserAssemblies(probeDirs);
  var targetAssembly = FindAssembly(userAssemblies, targetAssemblyPath)
    ?? throw new InvalidOperationException($"Failed to load target assembly '{targetAssemblyPath}'");
  var startupAssembly = startupAssemblyPath is not null
    ? FindAssembly(userAssemblies, startupAssemblyPath) ?? targetAssembly
    : targetAssembly;

  var contextType = ResolveContextType(userAssemblies, contextTypeName)
    ?? throw new InvalidOperationException($"DbContext type '{contextTypeName}' was not found in the build output");

  using var context = CreateDbContext(contextType, startupAssembly);

  var sql = await TranslateQueryAsync(queryText, contextTypeName, locals, usings, context, userAssemblies, probeDirs);

  Console.WriteLine(JsonSerializer.Serialize(new { sql }));
  return 0;
}
catch (Exception ex)
{
  var error = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
  Console.WriteLine(JsonSerializer.Serialize(new { error = error.Message }));
  return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
  var result = new Dictionary<string, string>();
  for (var i = 0; i + 1 < args.Length; i += 2)
  {
    result[args[i]] = args[i + 1];
  }
  return result;
}

static List<Assembly> LoadUserAssemblies(List<string> probeDirs)
{
  var assemblies = new List<Assembly>();
  var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach (var dll in probeDirs.SelectMany(x => Directory.GetFiles(x, "*.dll")))
  {
    if (!seen.Add(Path.GetFileName(dll)))
    {
      continue;
    }
    try
    {
      assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(dll));
    }
    catch
    {
      // Native libraries, resource dlls or assemblies already loaded by the host are skipped
    }
  }
  return assemblies;
}

static Assembly? FindAssembly(List<Assembly> assemblies, string assemblyPath) =>
  assemblies.FirstOrDefault(x => string.Equals(x.Location, assemblyPath, StringComparison.OrdinalIgnoreCase))
  ?? assemblies.FirstOrDefault(x => string.Equals(Path.GetFileName(x.Location), Path.GetFileName(assemblyPath), StringComparison.OrdinalIgnoreCase));

static Type? ResolveContextType(List<Assembly> assemblies, string contextTypeName) =>
  assemblies
    .SelectMany(x =>
    {
      try
      {
        return x.GetTypes();
      }
      catch
      {
        return Type.EmptyTypes;
      }
    })
    .FirstOrDefault(x => x.FullName == contextTypeName || x.FullName?.Replace('+', '.') == contextTypeName);

static IDisposable CreateDbContext(Type contextType, Assembly startupAssembly)
{
  Assembly designAssembly;
  try
  {
    designAssembly = Assembly.Load(new AssemblyName("Microsoft.EntityFrameworkCore.Design"));
  }
  catch (Exception ex)
  {
    throw new InvalidOperationException("The project must reference the Microsoft.EntityFrameworkCore.Design package (same requirement as dotnet-ef)", ex);
  }

  var activator = designAssembly.GetType("Microsoft.EntityFrameworkCore.Design.DbContextActivator")
    ?? throw new InvalidOperationException("DbContextActivator was not found in Microsoft.EntityFrameworkCore.Design");

  var createInstance = activator
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .First(x => x.Name == "CreateInstance" && x.GetParameters().Length == 3);

  return (IDisposable)createInstance.Invoke(null, [contextType, startupAssembly, null])!;
}

static async Task<string> TranslateQueryAsync(
  string queryText,
  string contextTypeName,
  List<LocalVariable> locals,
  List<string> usings,
  IDisposable context,
  List<Assembly> userAssemblies,
  List<string> probeDirs)
{
  using var loader = new InteractiveAssemblyLoader();
  userAssemblies.ForEach(loader.RegisterDependency);

  var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  var references = probeDirs
    .SelectMany(x => Directory.GetFiles(x, "*.dll"))
    .Where(x => seen.Add(Path.GetFileName(x)))
    .Where(x =>
    {
      try
      {
        AssemblyName.GetAssemblyName(x);
        return true;
      }
      catch
      {
        return false;
      }
    })
    .ToArray();

  var options = ScriptOptions.Default
    .AddReferences(references)
    .AddImports("System", "System.Linq", "Microsoft.EntityFrameworkCore");

  var usingDirectives = string.Join(Environment.NewLine, usings);
  var localDeclarations = string.Join(Environment.NewLine, locals.Select(x => $"{x.Type} {x.Name} = default;"));
  var code = $"""
    {usingDirectives}
    var __ctx = ({contextTypeName})CtxObj;
    {localDeclarations}
    return ({queryText}).ToQueryString();
    """;

  var script = CSharpScript.Create<string>(code, options, typeof(EfQueryGlobals), loader);
  var result = await script.RunAsync(new EfQueryGlobals { CtxObj = context });
  return result.ReturnValue;
}

namespace EasyDotnet.EfQueryRunner
{
  public sealed record LocalVariable(string Name, string Type);

  public class EfQueryGlobals
  {
    public object CtxObj { get; set; } = null!;
  }
}