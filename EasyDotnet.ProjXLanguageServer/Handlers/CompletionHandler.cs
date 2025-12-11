using System.Xml;
using System.Xml.Linq;
using EasyDotnet.MsBuild;
using EasyDotnet.ProjXLanguageServer.Services;
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

  private CursorPosition GetCompletionContext(string xml, int line, int character)
  {
    try
    {
      var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
      var position = GetAbsolutePosition(xml, line, character);

      // Find the element at cursor position
      var element = FindElementAtPosition(doc.Root!, position, xml);

      if (element == null)
        return CursorPosition.Unknown;

      // Check if we're inside a tag value (between > and <)
      if (IsInsideElementValue(xml, position))
        return CursorPosition.InsidePropertyValue;

      // Check parent hierarchy
      return DetermineContextFromHierarchy(element);
    }
    catch
    {
      return CursorPosition.Unknown;
    }
  }

  private CursorPosition DetermineContextFromHierarchy(XElement element)
  {
    var parent = element.Parent;

    // At root level
    if (parent == null || parent.Name.LocalName == "Project")
    {
      if (element.Name.LocalName == "Project")
        return CursorPosition.ProjectRoot;
      return CursorPosition.ProjectRoot;
    }

    // Inside PropertyGroup
    if (parent.Name.LocalName == "PropertyGroup")
      return CursorPosition.PropertyGroup;

    // Inside ItemGroup
    if (parent.Name.LocalName == "ItemGroup")
      return CursorPosition.ItemGroup;

    return CursorPosition.Unknown;
  }

  private bool IsInsideElementValue(string xml, int position)
  {
    if (position >= xml.Length) return false;

    // Look backwards for < or >
    var i = position - 1;
    while (i >= 0)
    {
      if (xml[i] == '>') return true;  // We're after a closing >
      if (xml[i] == '<') return false; // We're in a tag
      i--;
    }
    return false;
  }

  private XElement? FindElementAtPosition(XElement root, int position, string xml)
  {
    // Find the deepest element that contains the position
    foreach (var element in root.DescendantsAndSelf())
    {
      var lineInfo = (IXmlLineInfo)element;
      if (!lineInfo.HasLineInfo()) continue;

      var elementText = element.ToString();
      var startPos = GetAbsolutePosition(xml, lineInfo.LineNumber - 1, lineInfo.LinePosition - 1);
      var endPos = startPos + elementText.Length;

      if (position >= startPos && position <= endPos)
      {
        // Check children first (deepest match)
        foreach (var child in element.Elements())
        {
          var childResult = FindElementAtPosition(child, position, xml);
          if (childResult != null) return childResult;
        }
        return element;
      }
    }
    return null;
  }

  private int GetAbsolutePosition(string text, int line, int character)
  {
    var pos = 0;
    var currentLine = 0;

    for (var i = 0; i < text.Length; i++)
    {
      if (currentLine == line)
      {
        pos += character;
        break;
      }
      if (text[i] == '\n')
      {
        currentLine++;
      }
      pos++;
    }
    return pos;
  }

  private CompletionItem[] GetProjectRootCompletions() => [
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
                InsertTextFormat = InsertTextFormat.Snippet,
                Detail = "MSBuild ItemGroup"
            },
            new CompletionItem
            {
                Label = "Target",
                Kind = CompletionItemKind.Class,
                InsertText = "<Target Name=\"$1\">\n  $0\n</Target>",
                InsertTextFormat = InsertTextFormat.Snippet,
                Detail = "MSBuild Target"
            }
    ];

  private CompletionItem[] GetPropertyGroupCompletions() => [.. MsBuildProperties.GetAllPropertiesWithDocs()
        .Select(p => new CompletionItem
        {
          Label = p.Name,
          Kind = CompletionItemKind.Property,
          InsertText = $"<{p.Name}>$0</{p.Name}>",
          InsertTextFormat = InsertTextFormat.Snippet,
          Detail = "MSBuild Property",
          Documentation = new MarkupContent
          {
            Kind = MarkupKind.Markdown,
            Value = p.Description
          }
        })];

  private CompletionItem[] GetItemGroupCompletions() => [
        new CompletionItem
            {
                Label = "PackageReference",
                Kind = CompletionItemKind.Class,
                InsertText = "<PackageReference Include=\"$1\" Version=\"$2\" />",
                InsertTextFormat = InsertTextFormat.Snippet,
                Detail = "Package Reference",
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
                    Value = "Reference to another project"
                }
            },
            new CompletionItem
            {
                Label = "Reference",
                Kind = CompletionItemKind.Class,
                InsertText = "<Reference Include=\"$1\" />",
                InsertTextFormat = InsertTextFormat.Snippet,
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
                Kind = CompletionItemKind.Class,
                InsertText = "<None Include=\"$1\" />",
                InsertTextFormat = InsertTextFormat.Snippet,
                Detail = "None Item"
            },
            new CompletionItem
            {
                Label = "Content",
                Kind = CompletionItemKind.Class,
                InsertText = "<Content Include=\"$1\" />",
                InsertTextFormat = InsertTextFormat.Snippet,
                Detail = "Content Item"
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