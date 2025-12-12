using EasyDotnet.MsBuild;
using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.Language.Xml;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CompletionHandler(IDocumentManager documentManager, JsonRpc jsonRpc) : BaseController(jsonRpc)
{
  [JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]
  public CompletionList GetCompletion(CompletionParams completionParams)
  {
    var documentContent = documentManager.GetDocumentContent(completionParams.TextDocument.Uri);
    if (string.IsNullOrEmpty(documentContent))
      return new CompletionList { Items = [] };

    var context = GetCompletionContext(
        documentContent,
        completionParams.Position.Line,
        completionParams.Position.Character
    );

    var items = context switch
    {
      CursorPosition.ProjectRoot => GetProjectRootCompletions(),
      CursorPosition.PropertyGroup => GetPropertyGroupCompletions(),
      CursorPosition.ItemGroup => GetItemGroupCompletions(),
      CursorPosition.InsidePropertyValue => [],
      _ => []
    };

    return new CompletionList { Items = items };
  }

  private static CursorPosition GetCompletionContext(string xml, int line, int character)
  {
    try
    {
      var root = Parser.ParseText(xml);
      var position = GetAbsolutePosition(xml, line, character);

      var node = root.FindNode(position, includeTrivia: true);
      var currentElement = FindContainingElement(node);

      if (currentElement == null)
        return CursorPosition.Unknown;

      if (IsInsideStartTag(currentElement, position))
      {
        var parent = FindValidParentElement(currentElement);
        if (parent != null)
        {
          return GetContextForElement(parent);
        }
      }

      return GetContextForElement(currentElement);
    }
    catch
    {
      return CursorPosition.Unknown;
    }
  }

  private static CursorPosition GetContextForElement(XmlElementSyntax element)
  {
    var current = element;
    while (current != null)
    {
      var name = current.Name;
      if (!string.IsNullOrEmpty(name))
      {
        return name switch
        {
          "Project" => CursorPosition.ProjectRoot,
          "PropertyGroup" => CursorPosition.PropertyGroup,
          "ItemGroup" => CursorPosition.ItemGroup,
          _ => CursorPosition.Unknown
        };
      }
      current = FindParentElement(current);
    }
    return CursorPosition.Unknown;
  }

  private static XmlElementSyntax? FindValidParentElement(XmlElementSyntax element)
  {
    var current = FindParentElement(element);

    while (current != null && string.IsNullOrEmpty(current.Name))
    {
      current = FindParentElement(current);
    }

    return current;
  }

  private static XmlElementSyntax? FindContainingElement(SyntaxNode? node)
  {
    while (node != null)
    {
      if (node is XmlElementSyntax element)
        return element;
      node = node.Parent;
    }
    return null;
  }

  private static XmlElementSyntax? FindParentElement(XmlElementSyntax element)
  {
    var node = element.Parent;
    while (node != null)
    {
      if (node is XmlElementSyntax parent)
        return parent;
      node = node.Parent;
    }
    return null;
  }

  private static bool IsInsideStartTag(XmlElementSyntax element, int position)
  {
    var startTag = element.StartTag;
    if (startTag == null) return false;

    return position >= startTag.Start && position < startTag.Start + startTag.FullWidth;
  }

  private static int GetAbsolutePosition(string text, int line, int character)
  {
    var currentLine = 0;
    for (var i = 0; i < text.Length; i++)
    {
      if (currentLine == line)
        return i + character;
      if (text[i] == '\n')
        currentLine++;
    }
    return text.Length;
  }

  private static CompletionItem[] GetProjectRootCompletions() =>
  [
      new CompletionItem
        {
            Label = "PropertyGroup",
            Kind = CompletionItemKind.Class,
            InsertText = "<PropertyGroup>\n  $0\n</PropertyGroup>",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "MSBuild PropertyGroup"
        },
        new CompletionItem
        {
            Label = "ItemGroup",
            Kind = CompletionItemKind.Class,
            InsertText = "<ItemGroup>\n  $0\n</ItemGroup>",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "MSBuild ItemGroup"
        },
        new CompletionItem
        {
            Label = "Target",
            Kind = CompletionItemKind.Class,
            InsertText = "<Target Name=\"$1\">\n  $0\n</Target>",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "MSBuild Target"
        },
        new CompletionItem
        {
            Label = "Import",
            Kind = CompletionItemKind.Class,
            InsertText = "<Import Project=\"$1\" />",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "MSBuild Import"
        },
        new CompletionItem
        {
            Label = "Choose",
            Kind = CompletionItemKind.Class,
            InsertText = "<Choose>\n  <When Condition=\"$1\">\n    $0\n  </When>\n</Choose>",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "MSBuild Choose/When block"
        }
  ];

  private static CompletionItem[] GetPropertyGroupCompletions() =>
  [
      ..  MsBuildProperties.GetAllPropertiesWithDocs()
            .Select(p => new CompletionItem
            {
                Label = p.Name,
                Kind = CompletionItemKind.Property,
                InsertText = $"<{p.Name}>$0</{p.Name}>",
                InsertTextFormat = InsertTextFormat. Snippet,
                Detail = "MSBuild Property",
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind. Markdown,
                    Value = p. Description
                }
            })
  ];

  private static CompletionItem[] GetItemGroupCompletions() =>
  [
      new CompletionItem
        {
            Label = "PackageReference",
            Kind = CompletionItemKind. Class,
            InsertText = "<PackageReference Include=\"$1\" Version=\"$2\" />",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "NuGet Package Reference",
            Documentation = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = "Reference to a NuGet package"
            }
        },
        new CompletionItem
        {
            Label = "ProjectReference",
            Kind = CompletionItemKind.Class,
            InsertText = "<ProjectReference Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "Project Reference",
            Documentation = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = "Reference to another project in the solution"
            }
        },
        new CompletionItem
        {
            Label = "Reference",
            Kind = CompletionItemKind. Class,
            InsertText = "<Reference Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "Assembly Reference"
        },
        new CompletionItem
        {
            Label = "Compile",
            Kind = CompletionItemKind.Class,
            InsertText = "<Compile Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "Compile Item"
        },
        new CompletionItem
        {
            Label = "None",
            Kind = CompletionItemKind. Class,
            InsertText = "<None Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "None Item"
        },
        new CompletionItem
        {
            Label = "Content",
            Kind = CompletionItemKind.Class,
            InsertText = "<Content Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "Content Item"
        },
        new CompletionItem
        {
            Label = "EmbeddedResource",
            Kind = CompletionItemKind.Class,
            InsertText = "<EmbeddedResource Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "Embedded Resource"
        },
        new CompletionItem
        {
            Label = "Using",
            Kind = CompletionItemKind.Class,
            InsertText = "<Using Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat.Snippet,
            Detail = "Global Using directive"
        },
        new CompletionItem
        {
            Label = "InternalsVisibleTo",
            Kind = CompletionItemKind. Class,
            InsertText = "<InternalsVisibleTo Include=\"$1\" />",
            InsertTextFormat = InsertTextFormat. Snippet,
            Detail = "InternalsVisibleTo assembly"
        }
  ];
}

public enum CursorPosition
{
  Unknown,
  ProjectRoot,
  PropertyGroup,
  ItemGroup,
  InsidePropertyValue,
  ProjectAttribute,
  PropertyGroupAttribute,
  ItemGroupAttribute,
  PackageReferenceAttribute,
  ProjectReferenceAttribute
}