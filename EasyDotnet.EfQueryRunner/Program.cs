using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using EasyDotnet.EfQueryRunner;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

var daemonMode = args.Contains("--daemon");
var arguments = ParseArgs(args);

// User code (host builders, EF logging) may write to stdout; responses go through the captured
// writer so the line-based protocol stays clean.
var output = Console.Out;
Console.SetOut(TextWriter.Null);

var targetAssemblyPath = Path.GetFullPath(arguments["--target-assembly"]);
var startupAssemblyPath = arguments.TryGetValue("--startup-assembly", out var startupArg) ? Path.GetFullPath(startupArg) : null;

if (!daemonMode)
{
  var response = await HandleAsync(EnsureGeneration(null), QueryRequest.FromArgs(arguments));
  output.WriteLine(response);
  return 0;
}

Generation? generation = null;
output.WriteLine(JsonSerializer.Serialize(new { ready = true }));

string? line;
while ((line = Console.In.ReadLine()) is not null)
{
  if (string.IsNullOrWhiteSpace(line))
  {
    continue;
  }
  string response;
  try
  {
    var request = JsonSerializer.Deserialize<QueryRequest>(line)!;
    generation = EnsureGeneration(generation);
    response = await HandleAsync(generation, request);
  }
  catch (Exception ex)
  {
    response = JsonSerializer.Serialize(new { error = Unwrap(ex).Message });
  }
  output.WriteLine(response);
}
return 0;

Generation EnsureGeneration(Generation? current)
{
  if (current is not null && !current.IsStale())
  {
    return current;
  }
  current?.Dispose();
  return Generation.Load(targetAssemblyPath, startupAssemblyPath);
}

static async Task<string> HandleAsync(Generation generation, QueryRequest request)
{
  try
  {
    var sql = await generation.TranslateAsync(request);
    return JsonSerializer.Serialize(new { sql });
  }
  catch (Exception ex)
  {
    return JsonSerializer.Serialize(new { error = Unwrap(ex).Message });
  }
}

static Exception Unwrap(Exception ex) =>
  ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

static Dictionary<string, string> ParseArgs(string[] args)
{
  var result = new Dictionary<string, string>();
  for (var i = 0; i < args.Length - 1; i++)
  {
    if (args[i].StartsWith("--") && !args[i + 1].StartsWith("--"))
    {
      result[args[i]] = args[i + 1];
    }
  }
  return result;
}

namespace EasyDotnet.EfQueryRunner
{
  public sealed record LocalVariable(string Name, string Type);

  public sealed record QueryRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("contextType")] string ContextType,
    [property: System.Text.Json.Serialization.JsonPropertyName("queryB64")] string QueryB64,
    [property: System.Text.Json.Serialization.JsonPropertyName("localsB64")] string? LocalsB64,
    [property: System.Text.Json.Serialization.JsonPropertyName("usingsB64")] string? UsingsB64)
  {
    public static QueryRequest FromArgs(Dictionary<string, string> arguments) =>
      new(
        arguments["--context-type"],
        arguments["--query-b64"],
        arguments.TryGetValue("--locals-b64", out var locals) ? locals : null,
        arguments.TryGetValue("--usings-b64", out var usings) ? usings : null);
  }

  public class EfQueryGlobals
  {
    public object CtxObj { get; set; } = null!;
  }

  /// <summary>
  /// Loads the user's assemblies in isolation so the daemon can discard a generation and load a
  /// fresh one when the build output changes, while the process (and its JIT-warm Roslyn
  /// scripting engine) stays alive.
  /// </summary>
  internal sealed class UserAssemblyLoadContext(List<AssemblyDependencyResolver> resolvers, List<string> probeDirs)
    : AssemblyLoadContext(isCollectible: false)
  {
    protected override Assembly? Load(AssemblyName assemblyName)
    {
      var resolved = resolvers.Select(x => x.ResolveAssemblyToPath(assemblyName)).FirstOrDefault(x => x is not null);
      if (resolved is not null)
      {
        return LoadFromAssemblyPath(resolved);
      }
      var candidate = probeDirs.Select(x => Path.Combine(x, $"{assemblyName.Name}.dll")).FirstOrDefault(File.Exists);
      return candidate is not null ? LoadFromAssemblyPath(candidate) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
      var resolved = resolvers.Select(x => x.ResolveUnmanagedDllToPath(unmanagedDllName)).FirstOrDefault(x => x is not null);
      return resolved is not null ? System.Runtime.InteropServices.NativeLibrary.Load(resolved) : IntPtr.Zero;
    }
  }

  internal sealed class Generation : IDisposable
  {
    private readonly List<string> _probeDirs;
    private readonly List<Assembly> _assemblies;
    private readonly ScriptOptions _scriptOptions;
    private readonly InteractiveAssemblyLoader _loader;
    private readonly Dictionary<string, IDisposable> _contexts = [];
    private readonly DateTime _loadedAt;
    private readonly string? _startupAssemblyPath;

    private Generation(List<string> probeDirs, List<Assembly> assemblies, ScriptOptions scriptOptions, InteractiveAssemblyLoader loader, DateTime loadedAt, string? startupAssemblyPath)
    {
      _probeDirs = probeDirs;
      _assemblies = assemblies;
      _scriptOptions = scriptOptions;
      _loader = loader;
      _loadedAt = loadedAt;
      _startupAssemblyPath = startupAssemblyPath;
    }

    public static Generation Load(string targetAssemblyPath, string? startupAssemblyPath)
    {
      var loadedAt = DateTime.UtcNow;

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

      var alc = new UserAssemblyLoadContext(resolvers, probeDirs);
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
          assemblies.Add(alc.LoadFromAssemblyPath(dll));
        }
        catch
        {
          // Native libraries, resource dlls and other unloadable files are skipped
        }
      }

      var loader = new InteractiveAssemblyLoader();
      assemblies.ForEach(loader.RegisterDependency);

      var referencesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var references = probeDirs
        .SelectMany(x => Directory.GetFiles(x, "*.dll"))
        .Where(x => referencesSeen.Add(Path.GetFileName(x)))
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

      return new Generation(probeDirs, assemblies, options, loader, loadedAt, startupAssemblyPath);
    }

    public bool IsStale() =>
      _probeDirs.SelectMany(x => Directory.GetFiles(x, "*.dll")).Any(x => File.GetLastWriteTimeUtc(x) > _loadedAt);

    public async Task<string> TranslateAsync(QueryRequest request)
    {
      var queryText = Encoding.UTF8.GetString(Convert.FromBase64String(request.QueryB64));
      var locals = request.LocalsB64 is not null
        ? JsonSerializer.Deserialize<List<LocalVariable>>(Encoding.UTF8.GetString(Convert.FromBase64String(request.LocalsB64)))!
        : [];
      var usings = request.UsingsB64 is not null
        ? JsonSerializer.Deserialize<List<string>>(Encoding.UTF8.GetString(Convert.FromBase64String(request.UsingsB64)))!
        : [];

      if (!_contexts.TryGetValue(request.ContextType, out var context))
      {
        context = CreateDbContext(request.ContextType);
        _contexts[request.ContextType] = context;
      }

      var usingDirectives = string.Join(Environment.NewLine, usings);
      var localDeclarations = string.Join(Environment.NewLine, locals.Select(x => $"{x.Type} {x.Name} = default;"));
      var code = $"""
        {usingDirectives}
        var __ctx = ({request.ContextType})CtxObj;
        {localDeclarations}
        return ({queryText}).ToQueryString();
        """;

      var script = CSharpScript.Create<string>(code, _scriptOptions, typeof(EfQueryGlobals), _loader);
      var result = await script.RunAsync(new EfQueryGlobals { CtxObj = context });
      return result.ReturnValue;
    }

    private IDisposable CreateDbContext(string contextTypeName)
    {
      var contextType = _assemblies
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
        .FirstOrDefault(x => x.FullName == contextTypeName || x.FullName?.Replace('+', '.') == contextTypeName)
        ?? throw new InvalidOperationException($"DbContext type '{contextTypeName}' was not found in the build output");

      var designAssembly = _assemblies.FirstOrDefault(x => x.GetName().Name == "Microsoft.EntityFrameworkCore.Design")
        ?? throw new InvalidOperationException("The project must reference the Microsoft.EntityFrameworkCore.Design package (same requirement as dotnet-ef)");

      var activator = designAssembly.GetType("Microsoft.EntityFrameworkCore.Design.DbContextActivator")
        ?? throw new InvalidOperationException("DbContextActivator was not found in Microsoft.EntityFrameworkCore.Design");

      var createInstance = activator
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(x => x.Name == "CreateInstance" && x.GetParameters().Length == 3);

      var startupAssembly = _startupAssemblyPath is not null
        ? _assemblies.FirstOrDefault(x => string.Equals(Path.GetFileName(x.Location), Path.GetFileName(_startupAssemblyPath), StringComparison.OrdinalIgnoreCase)) ?? contextType.Assembly
        : contextType.Assembly;

      return (IDisposable)createInstance.Invoke(null, [contextType, startupAssembly, null])!;
    }

    public void Dispose()
    {
      foreach (var context in _contexts.Values)
      {
        try
        {
          context.Dispose();
        }
        catch
        {
          // Disposal failures of abandoned contexts are irrelevant
        }
      }
      _contexts.Clear();
      _loader.Dispose();
    }
  }
}
