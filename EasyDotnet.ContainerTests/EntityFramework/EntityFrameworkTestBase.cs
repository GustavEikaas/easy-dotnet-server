using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.ContainerTests.EntityFramework;

public abstract class EntityFrameworkTestBase<TContainer> : ContainerTestBase<TContainer>
  where TContainer : ServerContainer, new()
{
  protected const string MigrationId = "20260520083538_Add_Maintenance_Notification";
  protected const string MigrationName = "Add_Maintenance_Notification";
  protected const string InitialMigrationId = "20260519070000_Initial";
  protected const string InitialMigrationName = "Initial";

  private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);

  private readonly ConcurrentQueue<TestPromptSelectionRequest> _selectionRequests = new();

  private readonly Channel<(TestPickerRequest Request, TaskCompletionSource<string[]?> Reply)> _pickers =
    Channel.CreateUnbounded<(TestPickerRequest, TaskCompletionSource<string[]?>)>();

  private readonly Channel<TestOpenBufferRequest> _openBuffers =
    Channel.CreateUnbounded<TestOpenBufferRequest>();

  private readonly Channel<TestTrackedJob> _runCommands =
    Channel.CreateUnbounded<TestTrackedJob>();

  private readonly Channel<string> _displayMessages =
    Channel.CreateUnbounded<string>();

  private readonly Channel<string> _displayErrors =
    Channel.CreateUnbounded<string>();

  private readonly Channel<TestQuickFixItem[]> _quickFixSets =
    Channel.CreateUnbounded<TestQuickFixItem[]>();

  protected string? PromptStringResponse { get; set; }
  protected TestPromptSelectionRequest[] PromptSelectionRequests => [.. _selectionRequests];

  protected Task BeginListMigrations() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/migrations-list"), RequestTimeout);

  protected Task BeginApplyMigration() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/migrations-apply"), RequestTimeout);

  protected Task BeginAddMigration() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/migrations-add"), RequestTimeout);

  protected Task BeginRemoveMigration() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/migrations-remove"), RequestTimeout);

  protected Task BeginUpdateDatabase() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/database-update"), RequestTimeout);

  protected Task BeginDropDatabase() =>
    BeginCall(Container.Rpc.InvokeAsync("ef/database-drop"), RequestTimeout);

  protected async Task<TestPickerRequest> ReceivePickerAsync(Func<TestPickerRequest, string[]?> respond)
  {
    var (request, reply) = await ReceiveAsync(
      _pickers.Reader,
      "picker/pick",
      RequestTimeout,
      CollectPendingNotifications);

    reply.SetResult(respond(request));
    return request;
  }

  protected async Task<TestOpenBufferRequest> ReceiveOpenBufferAsync() =>
    await ReceiveAsync(
      _openBuffers.Reader,
      "openBuffer",
      RequestTimeout,
      CollectPendingNotifications);

  protected async Task<TestTrackedJob> ReceiveRunCommandAsync()
  {
    var job = await ReceiveAsync(
      _runCommands.Reader,
      "runCommandManaged",
      RequestTimeout,
      CollectPendingNotifications);

    await Container.Rpc.NotifyWithParameterObjectAsync("processExited", new { job.JobId, exitCode = 0 });
    return job;
  }

  protected async Task<string> ReceiveDisplayMessageAsync() =>
    await ReceiveAsync(
      _displayMessages.Reader,
      "displayMessage",
      RequestTimeout,
      CollectPendingNotifications);

  protected bool PickerNotReceived() =>
    IsNotReceived(_pickers.Reader);

  protected TempWorkspace CreateEfWorkspace(bool emptyMigrations = false)
  {
    var ws = new TempWorkspaceBuilder()
      .WithSolutionX()
      .WithProject("App")
      .Build();

    var project = ws.Project("App");
    File.WriteAllText(project.CsprojPath, File.ReadAllText(project.CsprojPath).Replace("net8.0", "net10.0"));

    var migrationsDir = Path.Combine(project.Dir, "Migrations");
    Directory.CreateDirectory(migrationsDir);

    File.WriteAllText(Path.Combine(migrationsDir, $"{InitialMigrationId}.cs"), """
      namespace App.Migrations;

      internal sealed class Initial
      {
      }
      """);

    File.WriteAllText(Path.Combine(migrationsDir, $"{MigrationId}.cs"), """
      namespace App.Migrations;

      internal sealed class AddMaintenanceNotification
      {
      }
      """);

    if (emptyMigrations)
      File.WriteAllText(Path.Combine(project.Dir, ".empty-migrations"), string.Empty);

    RunDotnetBuild(project.CsprojPath);
    return ws;
  }

  private string CollectPendingNotifications()
  {
    var sb = new StringBuilder();

    while (_displayErrors.Reader.TryRead(out var error))
      sb.Append($"\n  displayError: \"{error}\"");
    while (_displayMessages.Reader.TryRead(out var message))
      sb.Append($"\n  displayMessage: \"{message}\"");
    while (_quickFixSets.Reader.TryRead(out var items))
      foreach (var item in items)
        sb.Append($"\n  quickfix: {item.FileName}({item.LineNumber},{item.ColumnNumber}): {item.Text}");

    return sb.Length > 0 ? $"\nPending notifications:{sb}" : string.Empty;
  }

  private static void RunDotnetBuild(string csprojPath)
  {
    var psi = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" -c Debug --nologo")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    using var process = Process.Start(psi)
      ?? throw new InvalidOperationException("Failed to start `dotnet build`.");

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException(
        $"`dotnet build` failed for fixture project '{csprojPath}' with exit code {process.ExitCode}.\n" +
        $"stdout:\n{stdout}\nstderr:\n{stderr}");
    }
  }

  public override Task<string?> PromptSelectionAsync(TestPromptSelectionRequest request)
  {
    _selectionRequests.Enqueue(request);
    return Task.FromResult<string?>(request.Choices[0].Id);
  }

  public override Task<string?> PromptStringAsync(TestPromptStringRequest request) =>
    Task.FromResult(PromptStringResponse);

  public override async Task<TestPickerResult?> PickerPickAsync(TestPickerRequest request)
  {
    var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
    await _pickers.Writer.WriteAsync((request, tcs));
    var selectedIds = await tcs.Task;
    return selectedIds is null ? null : new TestPickerResult(selectedIds);
  }

  public override Task<bool> OpenBufferAsync(TestOpenBufferRequest request)
  {
    _openBuffers.Writer.TryWrite(request);
    return Task.FromResult(true);
  }

  public override Task<RunCommandResponse> RunCommandManagedAsync(TestTrackedJob job)
  {
    _runCommands.Writer.TryWrite(job);
    return Task.FromResult(new RunCommandResponse(0));
  }

  public override void DisplayMessage(TestDisplayMessage message) =>
    _displayMessages.Writer.TryWrite(message.Message);

  public override void DisplayError(TestDisplayMessage message) =>
    _displayErrors.Writer.TryWrite(message.Message);

  public override void SetQuickFix(TestQuickFixItem[] quickFixItems) =>
    _quickFixSets.Writer.TryWrite(quickFixItems);
}
