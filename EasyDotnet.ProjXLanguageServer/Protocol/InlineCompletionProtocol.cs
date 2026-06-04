using System.Runtime.Serialization;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Protocol;

/// <summary>
/// Inline completion options used during static registration.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionOptions
/// </summary>
[DataContract]
public class InlineCompletionOptions
{
  [DataMember(Name = "workDoneProgress", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public bool? WorkDoneProgress { get; init; }
}

/// <summary>
/// Inline completion options used during static or dynamic registration.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionRegistrationOptions
/// </summary>
[DataContract]
public sealed class InlineCompletionRegistrationOptions : InlineCompletionOptions
{
  [DataMember(Name = "documentSelector", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public DocumentFilter[]? DocumentSelector { get; init; }

  [DataMember(Name = "id", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public string? Id { get; init; }
}

/// <summary>
/// Client capabilities specific to inline completions.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionClientCapabilities
/// </summary>
[DataContract]
public sealed class InlineCompletionClientCapabilities
{
  [DataMember(Name = "dynamicRegistration", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public bool? DynamicRegistration { get; init; }
}

/// <summary>
/// A parameter literal used in inline completion requests.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionParams
/// </summary>
[DataContract]
public sealed class InlineCompletionParams : TextDocumentPositionParams
{
  [DataMember(Name = "context")]
  public required InlineCompletionContext Context { get; init; }

  [DataMember(Name = "workDoneToken", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public SumType<int, string>? WorkDoneToken { get; init; }
}

/// <summary>
/// Provides information about the context in which an inline completion was requested.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionContext
/// </summary>
[DataContract]
public sealed class InlineCompletionContext
{
  [DataMember(Name = "triggerKind")]
  public InlineCompletionTriggerKind TriggerKind { get; init; }

  [DataMember(Name = "selectedCompletionInfo", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public SelectedCompletionInfo? SelectedCompletionInfo { get; init; }
}

/// <summary>
/// Describes how an inline completion provider was triggered.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionTriggerKind
/// </summary>
public enum InlineCompletionTriggerKind
{
  Invoked = 1,
  Automatic = 2
}

/// <summary>
/// Describes the currently selected completion item.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#selectedCompletionInfo
/// </summary>
[DataContract]
public sealed class SelectedCompletionInfo
{
  [DataMember(Name = "range")]
  public required LspRange Range { get; init; }

  [DataMember(Name = "text")]
  public required string Text { get; init; }
}

/// <summary>
/// Represents a collection of inline completion items to be presented in the editor.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionList
/// </summary>
[DataContract]
public sealed class InlineCompletionList
{
  [DataMember(Name = "items")]
  public required InlineCompletionItem[] Items { get; init; }
}

/// <summary>
/// An inline completion item represents a text snippet proposed inline while editing.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#inlineCompletionItem
/// </summary>
[DataContract]
public sealed class InlineCompletionItem
{
  [DataMember(Name = "insertText")]
  public required SumType<string, StringValue> InsertText { get; init; }

  [DataMember(Name = "filterText", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public string? FilterText { get; init; }

  [DataMember(Name = "range", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public LspRange? Range { get; init; }

  [DataMember(Name = "command", IsRequired = false)]
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  public Command? Command { get; init; }
}

/// <summary>
/// A snippet string value for insertion.
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.18/specification/#stringValue
/// </summary>
[DataContract]
public sealed class StringValue
{
  [DataMember(Name = "kind")]
  public string Kind { get; init; } = "snippet";

  [DataMember(Name = "value")]
  public required string Value { get; init; }
}