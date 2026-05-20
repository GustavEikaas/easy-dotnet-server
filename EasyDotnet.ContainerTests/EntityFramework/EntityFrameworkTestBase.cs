using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using EasyDotnet.ContainerTests.Docker;
using EasyDotnet.ContainerTests.Scaffold;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;

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

  protected override void ConfigureRpc(JsonRpc rpc) =>
    rpc.AddLocalRpcTarget(new RpcHandlers(this), new JsonRpcTargetOptions { DisposeOnDisconnect = false });

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

  private sealed class RpcHandlers(EntityFrameworkTestBase<TContainer> test)
  {
    [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptSelection(TestPromptSelectionRequest request)
    {
      test._selectionRequests.Enqueue(request);
      return Task.FromResult<string?>(request.Choices[0].Id);
    }

    [JsonRpcMethod("promptString", UseSingleObjectParameterDeserialization = true)]
    public Task<string?> PromptString(TestPromptStringRequest request) =>
      Task.FromResult(test.PromptStringResponse);

    [JsonRpcMethod("picker/pick", UseSingleObjectParameterDeserialization = true)]
    public async Task<TestPickerResult?> PickerPick(TestPickerRequest request)
    {
      var tcs = new TaskCompletionSource<string[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
      await test._pickers.Writer.WriteAsync((request, tcs));
      var selectedIds = await tcs.Task;
      return selectedIds is null ? null : new TestPickerResult(selectedIds);
    }

    [JsonRpcMethod("openBuffer", UseSingleObjectParameterDeserialization = true)]
    public Task<bool> OpenBuffer(TestOpenBufferRequest request)
    {
      test._openBuffers.Writer.TryWrite(request);
      return Task.FromResult(true);
    }

    [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
    public Task<RunCommandResponse> RunCommandManaged(TestTrackedJob job)
    {
      test._runCommands.Writer.TryWrite(job);
      return Task.FromResult(new RunCommandResponse(0));
    }

    [JsonRpcMethod("displayMessage", UseSingleObjectParameterDeserialization = true)]
    public void DisplayMessage(TestDisplayMessage message) =>
      test._displayMessages.Writer.TryWrite(message.Message);

    [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
    public void DisplayError(TestDisplayMessage message) =>
      test._displayErrors.Writer.TryWrite(message.Message);

    [JsonRpcMethod("quickfix/set")]
    public void SetQuickFix(TestQuickFixItem[] quickFixItems) =>
      test._quickFixSets.Writer.TryWrite(quickFixItems);
  }
}

public sealed class EfFakeDotnetSdk10LinuxContainer() : LinuxServerContainer("mcr.microsoft.com/dotnet/sdk:10.0")
{
  private readonly string _fakeBinPath = Path.Combine(Path.GetTempPath(), $"easydotnet-fake-dotnet-{Guid.NewGuid():N}");

  public override int SdkMajorVersion => 10;

  protected override ContainerBuilder ConfigureContainer(ContainerBuilder builder) =>
    base.ConfigureContainer(builder)
      .WithEnvironment("PATH", $"{_fakeBinPath}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin");

  protected override Task OnBeforeStartAsync(CancellationToken ct)
  {
    Directory.CreateDirectory(_fakeBinPath);
    var fakeDotnetEfPath = Path.Combine(_fakeBinPath, "dotnet-ef");

    File.WriteAllText(fakeDotnetEfPath, """
      #!/bin/sh

      project_path=
      prev=
      for arg in "$@"; do
        if [ "$prev" = "--project" ]; then
          project_path=$arg
          break
        fi
        prev=$arg
      done

      project_dir=
      if [ -n "$project_path" ]; then
        project_dir=$(dirname "$project_path")
      fi

      if [ "$1" = "dbcontext" ] && [ "$2" = "list" ]; then
        cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: [
      data:   {
      data:     "fullName": "App.AppDbContext",
      data:     "safeName": "AppDbContext",
      data:     "name": "AppDbContext",
      data:     "assemblyQualifiedName": "App.AppDbContext, App, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
      data:   }
      data: ]
      EOF
        exit 0
      fi

      if [ "$1" = "migrations" ] && [ "$2" = "list" ]; then
        if [ -n "$project_dir" ] && [ -f "$project_dir/.empty-migrations" ]; then
          cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: []
      EOF
          exit 0
        fi

        cat <<'EOF'
      info: Build started...
      info: Build succeeded.
      data: [
      data:   {
      data:     "id": "20260519070000_Initial",
      data:     "name": "Initial",
      data:     "safeName": "Initial",
      data:     "applied": true
      data:   },
      data:   {
      data:     "id": "20260520083538_Add_Maintenance_Notification",
      data:     "name": "Add_Maintenance_Notification",
      data:     "safeName": "Add_Maintenance_Notification",
      data:     "applied": true
      data:   }
      data: ]
      EOF
        exit 0
      fi

      echo "Unexpected dotnet-ef arguments: $@" >&2
      exit 1
      """);

    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(fakeDotnetEfPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    return Task.CompletedTask;
  }
}
