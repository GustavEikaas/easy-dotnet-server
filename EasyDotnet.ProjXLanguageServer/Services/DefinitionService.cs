using System.IO.Abstractions;
using System.Text.RegularExpressions;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IDefinitionService
{
  Location? GetDefinition(CsprojDocument doc, int line, int character);
}

public partial class DefinitionService(
    IUserSecretsResolver userSecretsResolver,
    IFileSystem fileSystem) : IDefinitionService
{
  private static readonly Regex GuidRegex = SecretGuidRegex();

  public Location? GetDefinition(CsprojDocument doc, int line, int character)
  {
    var ctx = XmlContextResolver.Resolve(doc, line, character);

    if (ctx.Kind == CursorContextKind.InsideElementText
        && string.Equals(ctx.ElementName, "UserSecretsId", StringComparison.Ordinal)
        && ctx.Element is XmlElementSyntax el)
    {
      return ResolveUserSecrets(el, doc.Text);
    }

    if (ctx.Kind == CursorContextKind.InsideAttributeValue && ctx.Element != null)
    {
      var include = GetAttributeValue(ctx.Element, "Include");
      var project = GetAttributeValue(ctx.Element, "Project");

      var elementName = ctx.ElementName;
      if (string.Equals(elementName, "ProjectReference", StringComparison.Ordinal)
          && string.Equals(ctx.AttributeName, "Include", StringComparison.Ordinal)
          && include != null)
      {
        return ResolveRelativeFile(doc.Uri, include);
      }

      if (string.Equals(elementName, "Import", StringComparison.Ordinal)
          && string.Equals(ctx.AttributeName, "Project", StringComparison.Ordinal)
          && project != null)
      {
        return ResolveRelativeFile(doc.Uri, ExpandThisFileDirectory(project, doc.Uri));
      }
    }

    return null;
  }

  private Location? ResolveUserSecrets(XmlElementSyntax element, string text)
  {
    var startTag = element.StartTag;
    var endTag = element.EndTag;
    if (startTag == null || endTag == null)
    {
      return null;
    }

    var contentStart = startTag.Start + startTag.FullWidth;
    var contentEnd = endTag.Start;
    if (contentEnd <= contentStart || contentEnd > text.Length)
    {
      return null;
    }

    var inner = text[contentStart..contentEnd].Trim();
    if (!GuidRegex.IsMatch(inner))
    {
      return null;
    }

    var path = userSecretsResolver.EnsureSecretsFile(inner);
    return new Location
    {
      Uri = new Uri(path),
      Range = ZeroRange()
    };
  }

  private Location? ResolveRelativeFile(Uri docUri, string relative)
  {
    if (string.IsNullOrWhiteSpace(relative))
    {
      return null;
    }

    var docPath = docUri.LocalPath;
    var docDir = fileSystem.Path.GetDirectoryName(docPath);
    if (string.IsNullOrEmpty(docDir))
    {
      return null;
    }

    var normalized = Path.DirectorySeparatorChar == '/' ? relative.Replace('\\', '/') : relative.Replace('/', '\\');
    var combined = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(docDir, normalized));
    if (!fileSystem.File.Exists(combined))
    {
      return null;
    }

    return new Location
    {
      Uri = new Uri(combined),
      Range = ZeroRange()
    };
  }

  private static string ExpandThisFileDirectory(string raw, Uri docUri)
  {
    var docDir = Path.GetDirectoryName(docUri.LocalPath) ?? string.Empty;
    return raw
      .Replace("$(MSBuildThisFileDirectory)", docDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
      .Replace("$(MSBuildProjectDirectory)", docDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
  }

  private static string? GetAttributeValue(IXmlElementSyntax element, string name)
  {
    foreach (var attr in element.Attributes)
    {
      if (string.Equals(attr.Name, name, StringComparison.Ordinal))
      {
        return attr.Value;
      }
    }
    return null;
  }

  private static LspRange ZeroRange() => new()
  {
    Start = new Position { Line = 0, Character = 0 },
    End = new Position { Line = 0, Character = 0 }
  };

  [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled)]
  private static partial Regex SecretGuidRegex();
}