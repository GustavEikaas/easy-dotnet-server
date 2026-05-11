using System.Collections.Concurrent;
using EasyDotnet.Debugger.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Dap;

public sealed class VariableLocationResolver(ILogger<VariableLocationResolver>? logger = null) : IVariableLocationResolver
{
  private static readonly IReadOnlyDictionary<string, VariableLocation> Empty
    = new Dictionary<string, VariableLocation>(0);

  private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

  public IReadOnlyDictionary<string, VariableLocation> Resolve(string filePath, int line)
  {
    try
    {
      var tree = GetTree(filePath);
      if (tree is null)
      {
        return Empty;
      }

      var text = tree.GetText();
      if (line < 1 || line > text.Lines.Count)
      {
        return Empty;
      }

      var position = text.Lines[line - 1].Start;
      var token = tree.GetRoot().FindToken(position);
      var node = token.Parent;
      if (node is null)
      {
        return Empty;
      }

      var result = new Dictionary<string, VariableLocation>(StringComparer.Ordinal);
      Walk(node, position, result);
      return result;
    }
    catch (Exception ex)
    {
      logger?.LogDebug(ex, "Variable location resolution failed for {path}:{line}", filePath, line);
      return Empty;
    }
  }

  private SyntaxTree? GetTree(string path)
  {
    try
    {
      if (string.IsNullOrEmpty(path) || !File.Exists(path))
      {
        return null;
      }

      var mtime = File.GetLastWriteTimeUtc(path);
      if (_cache.TryGetValue(path, out var entry) && entry.Mtime == mtime)
      {
        return entry.Tree;
      }

      var text = File.ReadAllText(path);
      var tree = CSharpSyntaxTree.ParseText(text, path: path);
      _cache[path] = new CacheEntry(mtime, tree);
      return tree;
    }
    catch (Exception ex)
    {
      logger?.LogDebug(ex, "Failed to parse syntax tree for {path}", path);
      return null;
    }
  }

  private static void Walk(SyntaxNode start, int position, Dictionary<string, VariableLocation> sink)
  {
    SyntaxNode? prev = null;
    for (var current = start; current is not null; prev = current, current = current.Parent)
    {
      AddIntroducedBy(current, sink);
      AddPrecedingSiblingLocals(current, prev, position, sink);
    }
  }

  private static void AddIntroducedBy(SyntaxNode node, Dictionary<string, VariableLocation> sink)
  {
    switch (node)
    {
      case MethodDeclarationSyntax m:
        AddParameters(m.ParameterList, sink);
        break;
      case LocalFunctionStatementSyntax lf:
        AddParameters(lf.ParameterList, sink);
        break;
      case ConstructorDeclarationSyntax c:
        AddParameters(c.ParameterList, sink);
        break;
      case OperatorDeclarationSyntax op:
        AddParameters(op.ParameterList, sink);
        break;
      case ConversionOperatorDeclarationSyntax cop:
        AddParameters(cop.ParameterList, sink);
        break;
      case ParenthesizedLambdaExpressionSyntax pl:
        AddParameters(pl.ParameterList, sink);
        break;
      case SimpleLambdaExpressionSyntax sl:
        AddIdentifier(sl.Parameter.Identifier, sink);
        break;
      case AnonymousMethodExpressionSyntax am when am.ParameterList is not null:
        AddParameters(am.ParameterList, sink);
        break;
      case ForEachStatementSyntax fe:
        AddIdentifier(fe.Identifier, sink);
        break;
      case CatchClauseSyntax cc when cc.Declaration is not null:
        AddIdentifier(cc.Declaration.Identifier, sink);
        break;
      case UsingStatementSyntax us when us.Declaration is not null:
        foreach (var v in us.Declaration.Variables)
        {
          AddIdentifier(v.Identifier, sink);
        }
        break;
      case ForStatementSyntax fs when fs.Declaration is not null:
        foreach (var v in fs.Declaration.Variables)
        {
          AddIdentifier(v.Identifier, sink);
        }
        break;
    }
  }

  private static void AddPrecedingSiblingLocals(
    SyntaxNode current,
    SyntaxNode? childTowardLeaf,
    int position,
    Dictionary<string, VariableLocation> sink)
  {
    switch (current)
    {
      case BlockSyntax block:
        ExtractFromStatements(block.Statements, childTowardLeaf, position, sink);
        break;
      case SwitchSectionSyntax section:
        ExtractFromStatements(section.Statements, childTowardLeaf, position, sink);
        break;
      case CompilationUnitSyntax unit:
        foreach (var member in unit.Members)
        {
          if (member is GlobalStatementSyntax gs && gs.SpanStart < position)
          {
            ExtractFromStatement(gs.Statement, sink);
          }
        }
        break;
    }
  }

  private static void ExtractFromStatements(
    IEnumerable<StatementSyntax> statements,
    SyntaxNode? stop,
    int position,
    Dictionary<string, VariableLocation> sink)
  {
    foreach (var stmt in statements)
    {
      if (stmt == stop)
      {
        break;
      }
      if (stmt.SpanStart >= position)
      {
        break;
      }
      ExtractFromStatement(stmt, sink);
    }
  }

  private static void ExtractFromStatement(StatementSyntax stmt, Dictionary<string, VariableLocation> sink)
  {
    if (stmt is LocalDeclarationStatementSyntax ld)
    {
      foreach (var v in ld.Declaration.Variables)
      {
        AddIdentifier(v.Identifier, sink);
      }
    }
    else if (stmt is LocalFunctionStatementSyntax lf)
    {
      AddIdentifier(lf.Identifier, sink);
    }
  }

  private static void AddParameters(ParameterListSyntax list, Dictionary<string, VariableLocation> sink)
  {
    foreach (var p in list.Parameters)
    {
      AddIdentifier(p.Identifier, sink);
    }
  }

  private static void AddIdentifier(SyntaxToken token, Dictionary<string, VariableLocation> sink)
  {
    var name = token.ValueText;
    if (string.IsNullOrEmpty(name) || sink.ContainsKey(name))
    {
      return;
    }
    var span = token.GetLocation().GetLineSpan();
    var path = span.Path;
    if (string.IsNullOrEmpty(path))
    {
      path = token.SyntaxTree?.FilePath ?? string.Empty;
    }
    sink[name] = new VariableLocation(
      path,
      span.StartLinePosition.Line + 1,
      span.StartLinePosition.Character + 1);
  }

  private readonly record struct CacheEntry(DateTime Mtime, SyntaxTree Tree);
}