using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class InitializeHandler(JsonRpc jsonRpc) : BaseController(jsonRpc)
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
          TriggerCharacters = ["<"]
        },
        TextDocumentSync = new TextDocumentSyncOptions
        {
          OpenClose = true,
          Change = TextDocumentSyncKind.Full
        }
      }
    };

    return Task.FromResult(result);
  }

  [JsonRpcMethod("initialized")]
  public void Initialized(InitializedParams param) => _ = LogAsync(MessageType.Info, "Client initialized.");

  [JsonRpcMethod("shutdown")]
  public Task Shutdown() => Task.CompletedTask;

  [JsonRpcMethod("exit")]
  public void Exit() => Environment.Exit(0);
}