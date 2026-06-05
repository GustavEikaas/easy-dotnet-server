using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.IDE.Models.Client;
using Microsoft.CodeAnalysis.CSharp;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests;

public abstract class NewFileCreateItemTests<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);

  private readonly Channel<(TestPickerRequest Request, TaskCompletionSource<string[]?> Reply)> _pickers =
    Channel.CreateUnbounded<(TestPickerRequest, TaskCompletionSource<string[]?>)>();

  private readonly Channel<TestOpenBufferRequest> _openBuffers =
    Channel.CreateUnbounded<TestOpenBufferRequest>();

  private readonly Channel<string> _displayErrors =
    Channel.CreateUnbounded<string>();

  private readonly Channel<string> _displayWarnings =
    Channel.CreateUnbounded<string>();

  private readonly ConcurrentQueue<string> _calls = new();

  protected string? PromptStringResponse { get; set; }

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

  [Fact]
  public async Task CreateItem_Record_CreatesMissingDirectoryAndOpensFileAfterWorkspaceEdit()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App")
      .Build();
    await InitializeWorkspaceAsync(ws);

    PromptStringResponse = "Customer";
    var outputPath = Path.Combine(ws.Project("App").Dir, "Models", "Generated");

    var task = BeginCreateItem(outputPath);
    var picker = await ReceivePickerAsync(_ => ["record"]);
    await task;

    var filePath = Path.Combine(outputPath, "Customer.cs");
    var opened = await ReceiveOpenBufferAsync();

    Assert.Equal(["1. Enum", "2. Record", "3. Interface", "4. Class"], picker.Choices.Select(x => x.Display).ToArray());
    Assert.True(Directory.Exists(outputPath));
    Assert.True(File.Exists(filePath));
    var generatedCode = await File.ReadAllTextAsync(filePath);
    Assert.Contains("public record Customer();", generatedCode);
    Assert.Empty(CSharpSyntaxTree.ParseText(generatedCode).GetDiagnostics());
    Assert.Equal(filePath, opened.Path);
    Assert.Equal(["applyWorkspaceEdit", "openBuffer"], _calls.ToArray());
  }

  [Fact]
  public async Task CreateItem_NoProject_BailsBeforePicker()
  {
    using var ws = new TempWorkspaceBuilder().Build();
    await InitializeWorkspaceAsync(ws);

    var task = BeginCreateItem(Path.Combine(ws.RootDir, "Models"));
    var error = await ReceiveDisplayErrorAsync();
    await task;

    Assert.Contains("No .csproj file found", error);
    Assert.True(IsNotReceived(_pickers.Reader));
  }

  [Fact]
  public async Task CreateItem_InvalidTypeName_DoesNotApplyEdit()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App")
      .Build();
    await InitializeWorkspaceAsync(ws);

    PromptStringResponse = "Models/Customer";

    var task = BeginCreateItem(ws.Project("App").Dir);
    await ReceivePickerAsync(_ => ["class"]);
    var warning = await ReceiveDisplayWarningAsync();
    await task;

    Assert.Contains("Invalid C# type name", warning);
    Assert.DoesNotContain("applyWorkspaceEdit", _calls);
  }

  [Fact]
  public async Task CreateItem_NonEmptyExistingFile_DoesNotOverwrite()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App")
      .Build();
    await InitializeWorkspaceAsync(ws);

    var filePath = Path.Combine(ws.Project("App").Dir, "Customer.cs");
    await File.WriteAllTextAsync(filePath, "existing");
    PromptStringResponse = "Customer";

    var task = BeginCreateItem(ws.Project("App").Dir);
    await ReceivePickerAsync(_ => ["class"]);
    var warning = await ReceiveDisplayWarningAsync();
    await task;

    Assert.Contains("File already exists", warning);
    Assert.Equal("existing", await File.ReadAllTextAsync(filePath));
    Assert.DoesNotContain("applyWorkspaceEdit", _calls);
  }

  [Fact]
  public async Task CreateItem_RelativeOutputPath_FailsRpc()
  {
    using var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App")
      .Build();
    await InitializeWorkspaceAsync(ws);

    await Assert.ThrowsAsync<RemoteInvocationException>(async () =>
      await Container.Rpc.InvokeWithParameterObjectAsync(
        "new-file/create-item",
        new { outputPath = "relative/path", preferFileScopedNamespace = false }));
  }

  private Task BeginCreateItem(string outputPath) =>
    BeginCall(
      Container.Rpc.InvokeWithParameterObjectAsync(
        "new-file/create-item",
        new { outputPath, preferFileScopedNamespace = false }),
      RequestTimeout);

  private async Task<TestPickerRequest> ReceivePickerAsync(Func<TestPickerRequest, string[]?> respond)
  {
    var (request, reply) = await ReceiveAsync(
      _pickers.Reader,
      "picker/pick",
      RequestTimeout,
      CollectPendingNotifications);

    reply.SetResult(respond(request));
    return request;
  }

  private async Task<TestOpenBufferRequest> ReceiveOpenBufferAsync() =>
    await ReceiveAsync(
      _openBuffers.Reader,
      "openBuffer",
      RequestTimeout,
      CollectPendingNotifications);

  private async Task<string> ReceiveDisplayErrorAsync() =>
    await ReceiveAsync(
      _displayErrors.Reader,
      "displayError",
      RequestTimeout,
      CollectPendingNotifications);

  private async Task<string> ReceiveDisplayWarningAsync() =>
    await ReceiveAsync(
      _displayWarnings.Reader,
      "displayWarning",
      RequestTimeout,
      CollectPendingNotifications);

  private string CollectPendingNotifications()
  {
    var sb = new StringBuilder();
    while (_displayErrors.Reader.TryRead(out var error))
      sb.Append($"\n  displayError: \"{error}\"");
    while (_displayWarnings.Reader.TryRead(out var warning))
      sb.Append($"\n  displayWarning: \"{warning}\"");
    return sb.Length > 0 ? $"\nPending notifications:{sb}" : string.Empty;
  }

  private sealed class RpcHandlers(NewFileCreateItemTests<TContainer> test)
  {
    [JsonRpcMethod("picker/pick", UseSingleObjectParameterDeserialization = true)]
    public async Task<TestPickerResult?> PickerPick(TestPickerRequest request)
    {
      var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
      await test._pickers.Writer.WriteAsync((request, tcs));
      var selectedIds = await tcs.Task;
      return selectedIds is null ? null : new TestPickerResult(selectedIds);
    }

    [JsonRpcMethod("promptString", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptString(TestPromptStringRequest request) =>
      Task.FromResult(test.PromptStringResponse);

    [JsonRpcMethod("applyWorkspaceEdit", UseSingleObjectParameterDeserialization = true)]
    public Task<bool> ApplyWorkspaceEdit(WorkspaceEdit edit)
    {
      test._calls.Enqueue("applyWorkspaceEdit");
      foreach (var change in edit.DocumentChanges)
      {
        var path = new Uri(change.TextDocument.Uri).LocalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, change.Edits.Single().NewText);
      }

      return Task.FromResult(true);
    }

    [JsonRpcMethod("openBuffer", UseSingleObjectParameterDeserialization = true)]
    public Task<bool> OpenBuffer(TestOpenBufferRequest request)
    {
      if (!File.Exists(request.Path))
        throw new FileNotFoundException("openBuffer called before file existed", request.Path);

      test._calls.Enqueue("openBuffer");
      test._openBuffers.Writer.TryWrite(request);
      return Task.FromResult(true);
    }

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) =>
      test._displayErrors.Writer.TryWrite(message.Message);

    [JsonRpcMethod("displayWarning", UseSingleObjectParameterDeserialization = true)]
    public void DisplayWarning(TestDisplayMessage message) =>
      test._displayWarnings.Writer.TryWrite(message.Message);
  }
}

public sealed class NewFileCreateItemSdk8Linux : NewFileCreateItemTests<Sdk8LinuxContainer>;
