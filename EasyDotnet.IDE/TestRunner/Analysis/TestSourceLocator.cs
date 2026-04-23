using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.IDE.TestRunner.Analysis;

/// <summary>
/// Positions of a test method or class within its source file.
/// All line numbers are 0-based to match Neovim extmark conventions.
/// </summary>
public record TestMethodLocation(
    int SignatureLine,   // [Fact] / [Test] attribute line (or class keyword line) — where the gutter sign goes
    int BodyStartLine,   // first line inside the opening brace — where go-to-definition lands
    int EndLine          // closing brace line
);

/// <summary>
/// A method that carries at least one known test attribute but is not yet
/// present in the compiled assembly — e.g. a test the user just wrote.
/// <see cref="NamespaceParts"/> is the dotted namespace of the enclosing
/// class, split on '.' — empty for types in the global namespace.
/// <see cref="ClassLocation"/> is the enclosing class's position.
/// </summary>
public record ProbableMethod(
    IReadOnlyList<string> NamespaceParts,
    string ClassName,
    TestMethodLocation ClassLocation,
    string MethodName,
    TestMethodLocation Location
);

/// <summary>
/// Parsed locations for a single source file.
/// </summary>
public record ParsedFileLocations(
    Dictionary<string, TestMethodLocation> Methods,
    Dictionary<string, TestMethodLocation> Classes,
    List<ProbableMethod> ProbableMethods
);

/// <summary>
/// Parses C# source files with Roslyn to locate test method and class positions.
/// Results are cached per file path so a file containing N tests is only parsed once
/// per discovery pass.
///
/// Usage: create one instance per DiscoverProjectAsync call, discard after.
/// For syncFile (in-memory content), use the static ParseContent method directly.
/// </summary>
public class TestSourceLocator
{
  // filepath → parsed locations
  private readonly Dictionary<string, ParsedFileLocations> _cache = new();

  /// <summary>
  /// Returns the source location for a test method by reading from disk (cached).
  /// </summary>
  public TestMethodLocation? Locate(string? filePath, string methodName)
  {
    if (filePath is null) return null;
    var parsed = GetOrParseFile(filePath);
    return parsed is null ? null : LookupMethod(parsed.Methods, methodName);
  }

  /// <summary>
  /// Returns the source location for a test class by reading from disk (cached).
  /// </summary>
  public TestMethodLocation? LocateClass(string? filePath, string className)
  {
    if (filePath is null) return null;
    var parsed = GetOrParseFile(filePath);
    return parsed?.Classes.TryGetValue(className, out var loc) == true ? loc : null;
  }

  /// <summary>
  /// Parses in-memory source content and returns all method and class locations.
  /// Used by syncFile — does NOT touch the file cache.
  /// </summary>
  public static ParsedFileLocations ParseContent(string content)
      => ParseSource(content);

  /// <summary>
  /// Looks up a single method in a pre-parsed content map.
  /// Handles argument-suffixed names like "MyTest(1, 2)" → "MyTest".
  /// </summary>
  public static TestMethodLocation? LookupMethod(
      Dictionary<string, TestMethodLocation> map, string methodName)
  {
    var key = NormalizeMethodKey(methodName);
    return map.TryGetValue(key, out var loc) ? loc : null;
  }

  private static string NormalizeMethodKey(string name)
  {
    name = name.Trim();
    var parenIdx = name.IndexOf('(');
    if (parenIdx >= 0) name = name[..parenIdx];
    var genericIdx = name.IndexOf('<');
    if (genericIdx >= 0) name = name[..genericIdx];
    var lastDot = name.LastIndexOf('.');
    if (lastDot >= 0) name = name[(lastDot + 1)..];

    return name.Trim();
  }

  // ---------------------------------------------------------------------------

  private ParsedFileLocations? GetOrParseFile(string filePath)
  {
    if (_cache.TryGetValue(filePath, out var cached)) return cached;

    string source;
    try { source = File.ReadAllText(filePath); }
    catch
    {
      _cache[filePath] = new ParsedFileLocations([], [], []);
      return null;
    }

    var parsed = ParseSource(source);
    _cache[filePath] = parsed;
    return parsed;
  }

  private static readonly HashSet<string> TestAttributeNames = new(StringComparer.Ordinal)
  {
    "Fact", "Theory", "Test", "TestCase", "TestCaseSource", "TestMethod",
    "InlineData", "DataRow", "Arguments",
  };

  private static bool HasTestAttribute(MethodDeclarationSyntax method) =>
      method.AttributeLists
          .SelectMany(al => al.Attributes)
          .Any(a =>
          {
            var name = a.Name switch
            {
              QualifiedNameSyntax q => q.Right.Identifier.Text,
              IdentifierNameSyntax i => i.Identifier.Text,
              _ => a.Name.ToString(),
            };
            return TestAttributeNames.Contains(name);
          });

  private static ClassDeclarationSyntax? EnclosingClass(MethodDeclarationSyntax method) =>
      method.Parent as ClassDeclarationSyntax;

  private static IReadOnlyList<string> EnclosingNamespaceParts(SyntaxNode node)
  {
    var parts = new List<string>();
    for (var current = node.Parent; current is not null; current = current.Parent)
    {
      if (current is BaseNamespaceDeclarationSyntax ns)
        parts.InsertRange(0, ns.Name.ToString().Split('.', StringSplitOptions.RemoveEmptyEntries));
    }
    return parts;
  }

  private static ParsedFileLocations ParseSource(string source)
  {
    var tree = CSharpSyntaxTree.ParseText(source);
    var root = tree.GetRoot();

    var methods = new Dictionary<string, TestMethodLocation>(StringComparer.Ordinal);
    var classes = new Dictionary<string, TestMethodLocation>(StringComparer.Ordinal);
    var classLocations = new Dictionary<string, TestMethodLocation>(StringComparer.Ordinal);
    var probableMethods = new List<ProbableMethod>();

    foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
    {
      var signatureNode = cls.AttributeLists.Count > 0
          ? (SyntaxNode)cls.AttributeLists[0]
          : cls;
      var signatureLine = signatureNode.GetLocation()
          .GetLineSpan().StartLinePosition.Line;

      var firstMember = cls.Members.FirstOrDefault();
      var bodyStartLine = firstMember is not null
          ? firstMember.GetLocation().GetLineSpan().StartLinePosition.Line
          : cls.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line;

      var endLine = cls.GetLocation().GetLineSpan().EndLinePosition.Line;
      classLocations[cls.Identifier.Text] = new TestMethodLocation(signatureLine, bodyStartLine, endLine);
    }

    foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
      var name = method.Identifier.Text;

      var signatureNode = method.AttributeLists.Count > 0
          ? (SyntaxNode)method.AttributeLists[0]
          : method;

      var signatureLine = signatureNode.GetLocation()
          .GetLineSpan().StartLinePosition.Line;

      var body = method.Body;
      int bodyStartLine;
      if (body is not null)
      {
        var firstToken = body.OpenBraceToken.GetNextToken();
        bodyStartLine = firstToken.IsKind(SyntaxKind.CloseBraceToken)
            ? body.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line
            : firstToken.GetLocation().GetLineSpan().StartLinePosition.Line;
      }
      else
      {
        bodyStartLine = method.ExpressionBody?
            .GetLocation().GetLineSpan().StartLinePosition.Line
            ?? signatureLine;
      }

      var endLine = method.GetLocation().GetLineSpan().EndLinePosition.Line;
      var loc = new TestMethodLocation(signatureLine, bodyStartLine, endLine);

      methods[name] = loc;

      if (HasTestAttribute(method) && EnclosingClass(method) is { } cls
          && classLocations.TryGetValue(cls.Identifier.Text, out var clsLoc))
      {
        var namespaceParts = EnclosingNamespaceParts(cls);
        probableMethods.Add(new ProbableMethod(namespaceParts, cls.Identifier.Text, clsLoc, name, loc));
      }
    }

    foreach (var (name, loc) in classLocations)
      classes[name] = loc;

    return new ParsedFileLocations(methods, classes, probableMethods);
  }
}