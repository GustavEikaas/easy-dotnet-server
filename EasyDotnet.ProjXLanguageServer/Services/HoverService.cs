using System.Text.RegularExpressions;
using EasyDotnet.MsBuild;
using EasyDotnet.ProjXLanguageServer.Utils;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IHoverService
{
  Hover? GetHover(CsprojDocument doc, int line, int character);
}

public partial class HoverService(IUserSecretsResolver userSecretsResolver) : IHoverService
{
  private static readonly Regex GuidRegex = SecretGuidRegex();

  public Hover? GetHover(CsprojDocument doc, int line, int character)
  {
    var ctx = XmlContextResolver.Resolve(doc, line, character);
    if (ctx.ElementName == null)
    {
      return null;
    }

    if (ctx.Kind == CursorContextKind.InsideElementText
        && string.Equals(ctx.ElementName, "UserSecretsId", StringComparison.Ordinal)
        && ctx.Element is Microsoft.Language.Xml.XmlElementSyntax el)
    {
      var inner = GetInnerText(el, doc.Text).Trim();
      if (GuidRegex.IsMatch(inner))
      {
        var path = userSecretsResolver.GetSecretsPath(inner);
        return new Hover
        {
          Contents = new MarkupContent
          {
            Kind = MarkupKind.Markdown,
            Value = $"**UserSecretsId**: `{inner}`\n\nSecrets file: `{path}`\n\nUse go-to-definition to open."
          }
        };
      }
    }

    var info = MsBuildProperties.GetAllPropertiesWithDocs()
        .FirstOrDefault(p => string.Equals(p.Name, ctx.ElementName, StringComparison.Ordinal));
    if (info != null)
    {
      return new Hover
      {
        Contents = new MarkupContent
        {
          Kind = MarkupKind.Markdown,
          Value = $"**{info.Name}**\n\n{info.Description}"
        }
      };
    }

    return null;
  }

  private static string GetInnerText(Microsoft.Language.Xml.XmlElementSyntax element, string text)
  {
    var startTag = element.StartTag;
    var endTag = element.EndTag;
    if (startTag == null || endTag == null)
    {
      return string.Empty;
    }

    var contentStart = startTag.Start + startTag.FullWidth;
    var contentEnd = endTag.Start;
    if (contentEnd <= contentStart || contentEnd > text.Length)
    {
      return string.Empty;
    }

    return text.Substring(contentStart, contentEnd - contentStart);
  }

  [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled)]
  private static partial Regex SecretGuidRegex();
}