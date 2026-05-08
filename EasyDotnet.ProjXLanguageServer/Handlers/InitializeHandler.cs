using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class InitializeHandler(ISemanticTokensService semanticTokensService) : BaseController
{
  [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
  public Task<InitializeResult> InitializeAsync(InitializeParams param)
  {
    var result = new InitializeResult
    {
      Capabilities = new ServerCapabilities
      {
        CompletionProvider = new CompletionOptions
        {
          ResolveProvider = true,
          TriggerCharacters = ["<", ">", "\""]
        },
        TextDocumentSync = new TextDocumentSyncOptions
        {
          OpenClose = true,
          Change = TextDocumentSyncKind.Full
        },
        HoverProvider = true,
        DefinitionProvider = true,
        CodeActionProvider = true,
        DocumentFormattingProvider = true,
        SignatureHelpProvider = new SignatureHelpOptions
        {
          TriggerCharacters = ["\"", "="],
          RetriggerCharacters = ["\""],
        },
        SemanticTokensOptions = new SemanticTokensOptions
        {
          Legend = semanticTokensService.Legend,
          Full = true,
          Range = false,
        }
      }
    };

    return Task.FromResult(result);
  }

  [JsonRpcMethod("initialized")]
  public void Initialized(InitializedParams param)
  {
  }

  [JsonRpcMethod("shutdown")]
  public Task Shutdown() => Task.CompletedTask;

  [JsonRpcMethod("exit")]
  public void Exit() => Environment.Exit(0);
}