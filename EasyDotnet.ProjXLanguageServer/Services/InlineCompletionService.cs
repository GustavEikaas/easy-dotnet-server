using System.Text.RegularExpressions;
using EasyDotnet.ProjXLanguageServer.Protocol;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Services;

public interface IInlineCompletionService
{
  Task<InlineCompletionList> GetInlineCompletionsAsync(CsprojDocument doc, int line, int character, CancellationToken cancellationToken);
}

public sealed partial class InlineCompletionService(ICompletionService completionService) : IInlineCompletionService
{
  public async Task<InlineCompletionList> GetInlineCompletionsAsync(CsprojDocument doc, int line, int character, CancellationToken cancellationToken)
  {
    var offset = doc.ToOffset(line, character);
    if (!TryGetTagPrefix(doc.Text, offset, out var prefix))
    {
      return Empty();
    }

    var result = await completionService.GetCompletionsAsync(doc, line, character, cancellationToken);
    var completion = result.Items.FirstOrDefault(item =>
        item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    if (completion == null)
    {
      return Empty();
    }

    var insertText = ToPlainInsertText(completion);
    if (!insertText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || insertText.Length <= prefix.Length)
    {
      return Empty();
    }

    return new InlineCompletionList
    {
      Items =
      [
        new InlineCompletionItem
        {
          InsertText = insertText[prefix.Length..],
          FilterText = insertText
        }
      ]
    };
  }

  private static InlineCompletionList Empty() => new() { Items = [] };

  private static string ToPlainInsertText(CompletionItem item)
  {
    var text = item.InsertText ?? item.Label;
    return item.InsertTextFormat == InsertTextFormat.Snippet
        ? StripSnippetSyntax(text)
        : text;
  }

  private static string StripSnippetSyntax(string text)
  {
    text = PlaceholderRegex().Replace(text, match => match.Groups[2].Value);
    return TabStopRegex().Replace(text, string.Empty);
  }

  private static bool TryGetTagPrefix(string text, int offset, out string prefix)
  {
    prefix = string.Empty;
    if (offset <= 0)
    {
      return false;
    }

    var lt = text.LastIndexOf('<', offset - 1);
    if (lt < 0)
    {
      return false;
    }

    var gt = text.LastIndexOf('>', offset - 1);
    if (gt > lt)
    {
      return false;
    }

    if (lt + 1 < text.Length && text[lt + 1] is '/' or '!' or '?')
    {
      return false;
    }

    for (var i = lt + 1; i < offset; i++)
    {
      if (!IsTagNameChar(text[i]))
      {
        return false;
      }
    }

    prefix = text[(lt + 1)..offset];
    return prefix.Length > 0;
  }

  private static bool IsTagNameChar(char c) =>
      char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';

  [GeneratedRegex(@"\$\{(\d+)(?::([^}]*))?\}", RegexOptions.Compiled)]
  private static partial Regex PlaceholderRegex();

  [GeneratedRegex(@"\$\d+", RegexOptions.Compiled)]
  private static partial Regex TabStopRegex();
}
