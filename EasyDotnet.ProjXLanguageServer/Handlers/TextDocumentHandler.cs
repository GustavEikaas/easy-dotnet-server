using System.Collections.Concurrent;
using EasyDotnet.ProjXLanguageServer.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;

namespace EasyDotnet.ProjXLanguageServer.Handlers;

public class TextDocumentHandler(
    IDocumentManager documentManager,
    IDiagnosticsService diagnosticsService,
    IDiagnosticsPublisher diagnosticsPublisher) : BaseController
{
  private const int DebounceMilliseconds = 20;
  private readonly ConcurrentDictionary<Uri, CancellationTokenSource> _pending = new();

  [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidOpenTextDocument(DidOpenTextDocumentParams @params)
  {
    documentManager.OpenDocument(@params.TextDocument.Uri, @params.TextDocument.Text, @params.TextDocument.Version);
    return PublishDebouncedAsync(@params.TextDocument.Uri);
  }

  [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidChangeTextDocument(DidChangeTextDocumentParams @params)
  {
    if (@params.ContentChanges.Length == 0)
      return Task.CompletedTask;

    var change = @params.ContentChanges[^1];
    documentManager.UpdateDocument(@params.TextDocument.Uri, change.Text, @params.TextDocument.Version);
    return PublishDebouncedAsync(@params.TextDocument.Uri);
  }

  [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
  public Task OnDidCloseTextDocument(DidCloseTextDocumentParams @params)
  {
    if (_pending.TryRemove(@params.TextDocument.Uri, out var cts))
      cts.Cancel();
    documentManager.CloseDocument(@params.TextDocument.Uri);
    return diagnosticsPublisher.PublishAsync(@params.TextDocument.Uri, []);
  }

  private Task PublishDebouncedAsync(Uri uri)
  {
    var cts = new CancellationTokenSource();
    if (_pending.TryGetValue(uri, out var previous))
      previous.Cancel();
    _pending[uri] = cts;

    return Task.Run(async () =>
    {
      try
      {
        await Task.Delay(DebounceMilliseconds, cts.Token);
      }
      catch (OperationCanceledException)
      {
        return;
      }

      if (!_pending.TryGetValue(uri, out var current) || current != cts)
        return;
      _pending.TryRemove(new KeyValuePair<Uri, CancellationTokenSource>(uri, cts));

      var doc = documentManager.GetDocument(uri);
      if (doc == null)
        return;
      var diagnostics = diagnosticsService.GetDiagnostics(doc);
      await diagnosticsPublisher.PublishAsync(uri, diagnostics);
    });
  }
}