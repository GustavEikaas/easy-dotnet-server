using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.NewFile;

/// <summary>
/// Base for roslyn/bootstrap-file-v2 container tests.
///
/// Handles the <c>applyWorkspaceEdit</c> reverse request the server sends after generating
/// file content, captures the text, and exposes typed helpers for test assertions.
/// </summary>
public abstract class NewFileTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private readonly Channel<string> _appliedEdits = Channel.CreateUnbounded<string>();

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  /// <summary>
  /// Calls <c>roslyn/bootstrap-file-v2</c> and registers the call as the active RPC scope.
  /// </summary>
  protected Task<TestBootstrapFileResponse> BeginBootstrapAsync(
    string filePath, string kind = "Class", bool preferFileScopedNamespace = true)
    => Container.Rpc.InvokeWithParameterObjectAsync<TestBootstrapFileResponse>(
        "roslyn/bootstrap-file-v2",
        new { filePath, kind, preferFileScopedNamespace });

  /// <summary>
  /// Waits for the server to send <c>applyWorkspaceEdit</c> and returns the generated text.
  /// </summary>
  protected async Task<string> ReceiveAppliedTextAsync()
    => await ReceiveAsync(
        _appliedEdits.Reader,
        "applyWorkspaceEdit",
        TimeSpan.FromSeconds(30),
        () => string.Empty);

  private sealed class RpcHandlers(NewFileTestBase<TContainer> test)
  {
    [JsonRpcMethod("applyWorkspaceEdit", UseSingleObjectParameterDeserialization = true)]
    public bool ApplyWorkspaceEdit(TestWorkspaceEdit edit)
    {
      var text = edit.DocumentChanges
          .SelectMany(c => c.Edits)
          .Select(e => e.NewText)
          .FirstOrDefault() ?? string.Empty;

      test._appliedEdits.Writer.TryWrite(text);
      return true;
    }
  }
}

// ── Local RPC payload types ─────────────────────────────────────────────────

public sealed record TestBootstrapFileResponse(bool Success);

public sealed record TestWorkspaceEdit(TestWorkspaceDocumentChange[] DocumentChanges);
public sealed record TestWorkspaceDocumentChange(TestTextDocumentIdentifier TextDocument, TestTextEdit[] Edits);
public sealed record TestTextDocumentIdentifier(string Uri);
public sealed record TestTextEdit(TestTextEditRange Range, string NewText);
public sealed record TestTextEditRange(TestTextEditPosition Start, TestTextEditPosition End);
public sealed record TestTextEditPosition(int Line, int Character);
