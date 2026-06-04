using System.Runtime.Serialization;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json;

namespace EasyDotnet.ProjXLanguageServer.Protocol;

/// <summary>
/// Server capabilities including LSP 3.18 inline completion support missing from Microsoft.VisualStudio.LanguageServer.Protocol 17.2.8.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#serverCapabilities
/// </summary>
[DataContract]
public sealed class ProjXServerCapabilities : ServerCapabilities
{
  [DataMember(Name = "inlineCompletionProvider", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public InlineCompletionOptions InlineCompletionProvider { get; init; } = new();
}
