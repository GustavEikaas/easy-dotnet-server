using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Protocol;

public sealed class ProjXServerCapabilities : ServerCapabilities
{
  public InlineCompletionOptions InlineCompletionProvider { get; init; } = new();
}
