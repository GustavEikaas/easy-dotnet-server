using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjectLanguageServer.Handlers;

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
          TriggerCharacters = [".", ":"]
        }
      }
    };

    return Task.FromResult(result);
  }

  [JsonRpcMethod("initialized")]
  public void Initialized(InitializedParams param) => _ = LogAsync(MessageType.Error, "Client initialized.");


  [JsonRpcMethod("shutdown")]
  public Task Shutdown() => Task.CompletedTask;

  [JsonRpcMethod("exit")]
  public void Exit() => Environment.Exit(0);
}