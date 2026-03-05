using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EasyDotnet.IDE.TestRunner.Analysis;

/// <summary>
/// Positions of a test method within its source file.
/// All line numbers are 0-based to match Neovim extmark conventions.
/// </summary>
public record TestMethodLocation(
    int SignatureLine,   // [Fact] / [Test] attribute line — where the gutter sign goes
    int BodyStartLine,   // first line inside the opening brace — where go-to-definition lands
    int EndLine          // closing brace line
);

/// <summary>
/// Parses C# source files with Roslyn to locate test method positions.
/// Results are cached per file path so a file containing N tests is only parsed once
/// per discovery pass.
///
/// Usage: create one instance per DiscoverProjectAsync call, discard after.
/// </summary>
/// <summary>
/// Parses C# source files with Roslyn to locate test method positions.
/// Results are cached per file path so a file containing N tests is only parsed once
/// per discovery pass.
///
/// Usage: create one instance per DiscoverProjectAsync call, discard after.
/// For syncFile (in-memory content), use the static ParseContent method directly.
/// </summary>
public class TestSourceLocator
{
  // filepath → (method name → location)
  private readonly Dictionary<string, Dictionary<string, TestMethodLocation>> _cache = new();

  /// <summary>
  /// Returns the source location for a test method by reading from disk (cached).
  /// Returns null if the file path is missing, unreadable, or the method is not found.
  /// </summary>
  public TestMethodLocation? Locate(string? filePath, string methodName)
  {
    if (filePath is null) return null;

    var map = GetOrParseFile(filePath);
    if (map is null) return null;

    return Lookup(map, methodName);
  }

  /// <summary>
  /// Parses in-memory source content and returns all method locations.
  /// Used by syncFile — does NOT touch the file cache.
  /// </summary>
  public static Dictionary<string, TestMethodLocation> ParseContent(string content)
      => ParseSource(content);

  /// <summary>
  /// Looks up a single method in a pre-parsed content map.
  /// Handles argument-suffixed names like "MyTest(1, 2)" → "MyTest".
  /// </summary>
  public static TestMethodLocation? Lookup(
      Dictionary<string, TestMethodLocation> map, string methodName)
  {
    if (map.TryGetValue(methodName, out var loc)) return loc;

    var bare = methodName.Contains('(')
        ? methodName[..methodName.IndexOf('(')]
        : methodName;

    return map.TryGetValue(bare, out loc) ? loc : null;
  }

  // ---------------------------------------------------------------------------

  private Dictionary<string, TestMethodLocation>? GetOrParseFile(string filePath)
  {
    if (_cache.TryGetValue(filePath, out var cached)) return cached;

    string source;
    try
    {
      source = File.ReadAllText(filePath);
    }
    catch
    {
      _cache[filePath] = [];
      return null;
    }

    var map = ParseSource(source);
    _cache[filePath] = map;
    return map;
  }

  private static Dictionary<string, TestMethodLocation> ParseSource(string source)
  {
    var tree = CSharpSyntaxTree.ParseText(source);
    var root = tree.GetRoot();
    var result = new Dictionary<string, TestMethodLocation>(StringComparer.Ordinal);

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

      var endLine = method.GetLocation()
          .GetLineSpan().EndLinePosition.Line;

      result[name] = new TestMethodLocation(signatureLine, bodyStartLine, endLine);
    }

    return result;
  }
}