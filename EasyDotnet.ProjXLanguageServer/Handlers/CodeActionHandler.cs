using System.Xml;
using System.Xml.Linq;
using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class CodeActionHandler(JsonRpc jsonRpc, IDocumentManager documentManager) : BaseController(jsonRpc)
{
  [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
  public SumType<Command, CodeAction>[] GetCodeActions(CodeActionParams codeActionParams)
  {
    var actions = new List<SumType<Command, CodeAction>>();

    try
    {
      var emptyGroupDiagnostics = codeActionParams.Context.Diagnostics
          .Where(d =>
          {
            if (d.Code == null) return false;

            return d.Code.Value.Match(
              _ => false,
              stringCode => stringCode == DiagnosticsService.EmptyGroupDiagnosticCode);
          })
          .ToArray();

      if (emptyGroupDiagnostics.Length == 0)
        return [.. actions];

      var documentContent = documentManager.GetDocumentContent(codeActionParams.TextDocument.Uri);
      if (string.IsNullOrEmpty(documentContent))
        return [.. actions];

      foreach (var diagnostic in emptyGroupDiagnostics)
      {
        var codeAction = CreateRemoveEmptyGroupAction(
            codeActionParams.TextDocument.Uri,
            diagnostic,
            documentContent
        );

        if (codeAction != null)
          actions.Add(codeAction);
      }
    }
    catch (Exception ex)
    {
      _ = LogAsync(MessageType.Error, $"[CodeAction] Error:  {ex.Message}\n{ex.StackTrace}");
    }

    return [.. actions];
  }

  private CodeAction? CreateRemoveEmptyGroupAction(Uri uri, Diagnostic diagnostic, string documentContent)
  {
    try
    {
      var doc = XDocument.Parse(documentContent, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

      // Find empty ItemGroup or PropertyGroup elements
      var emptyGroups = doc.Descendants()
          .Where(e => (e.Name.LocalName == "ItemGroup" || e.Name.LocalName == "PropertyGroup")
                   && !e.HasElements
                   && string.IsNullOrWhiteSpace(e.Value))
          .ToList();

      // Try to match the diagnostic range with an empty group
      XElement? targetElement = null;
      foreach (var element in emptyGroups)
      {
        var lineInfo = (IXmlLineInfo)element;
        if (!lineInfo.HasLineInfo())
          continue;

        var elementLine = lineInfo.LineNumber - 1; // XmlLineInfo is 1-based

        // Check if this element is on the same line as the diagnostic
        if (elementLine == diagnostic.Range.Start.Line)
        {
          targetElement = element;
          break;
        }
      }

      if (targetElement == null)
        return null;

      // Calculate the range to delete (including whitespace/newlines)
      var deleteRange = GetDeletionRange(targetElement, documentContent);

      var edit = new WorkspaceEdit
      {
        Changes = new Dictionary<string, TextEdit[]>
        {
          [uri.ToString()] = [
            new TextEdit
            {
              Range = deleteRange,
              NewText = string.Empty
            }
          ]
        }
      };

      return new CodeAction
      {
        Title = $"Remove empty {targetElement.Name.LocalName}",
        Kind = CodeActionKind.QuickFix,
        Diagnostics = [diagnostic],
        Edit = edit
      };
    }
    catch (Exception ex)
    {
      _ = LogAsync(MessageType.Error, $"[CreateAction] Error: {ex.Message}\n{ex.StackTrace}");
      return null;
    }
  }

  private Microsoft.VisualStudio.LanguageServer.Protocol.Range GetDeletionRange(XElement element, string xml)
  {
    var lineInfo = (IXmlLineInfo)element;
    var startLine = lineInfo.LineNumber - 1; // Convert to 0-based
    var startChar = lineInfo.LinePosition - 2; // LinePosition points after '<', adjust to before

    var lines = xml.Split('\n');
    var elementString = element.ToString();
    var elementLines = elementString.Split('\n');

    var endLine = startLine + elementLines.Length - 1;
    var endChar = elementLines.Length == 1
        ? startChar + elementString.Length
        : elementLines[^1].Length;

    // Try to include the entire line if the element is alone on its line(s)
    if (startLine < lines.Length)
    {
      var lineBeforeElement = startChar > 0
          ? lines[startLine].Substring(0, startChar)
          : "";

      var lineAfterElement = endLine < lines.Length && endChar < lines[endLine].Length
          ? lines[endLine].Substring(endChar)
          : "";

      // If the element is alone on the line(s), delete the whole line(s) including newline
      if (string.IsNullOrWhiteSpace(lineBeforeElement) && string.IsNullOrWhiteSpace(lineAfterElement))
      {
        startChar = 0;
        if (endLine + 1 < lines.Length)
        {
          // Include the newline by moving to start of next line
          endLine++;
          endChar = 0;
        }
        else
        {
          // Last line - include everything
          endChar = lines[endLine].Length;
        }
      }
    }

    return new Microsoft.VisualStudio.LanguageServer.Protocol.Range
    {
      Start = new Position(startLine, Math.Max(0, startChar)),
      End = new Position(endLine, endChar)
    };
  }
}