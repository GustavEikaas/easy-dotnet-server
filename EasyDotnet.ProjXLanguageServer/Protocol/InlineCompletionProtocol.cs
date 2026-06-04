using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Protocol;

public sealed class InlineCompletionOptions
{
}

public sealed class InlineCompletionParams
{
  public required TextDocumentIdentifier TextDocument { get; init; }
  public required Position Position { get; init; }
  public InlineCompletionContext? Context { get; init; }
}

public sealed class InlineCompletionContext
{
  public int TriggerKind { get; init; }
  public SelectedCompletionInfo? SelectedCompletionInfo { get; init; }
}

public sealed class SelectedCompletionInfo
{
  public required LspRange Range { get; init; }
  public required string Text { get; init; }
}

public sealed class InlineCompletionList
{
  public required InlineCompletionItem[] Items { get; init; }
}

public sealed class InlineCompletionItem
{
  public required string InsertText { get; init; }
  public string? FilterText { get; init; }
  public LspRange? Range { get; init; }
  public Command? Command { get; init; }
}