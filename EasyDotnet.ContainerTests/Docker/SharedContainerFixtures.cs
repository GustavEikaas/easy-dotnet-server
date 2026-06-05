using EasyDotnet.ContainerTests.TestRunner;
using EasyDotnet.IDE.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.ContainerTests.Docker;

public static class ContainerCollections
{
  public const string Sdk8Linux = "ContainerTests.Sdk8Linux";
  public const string Sdk9Linux = "ContainerTests.Sdk9Linux";
  public const string Sdk10Linux = "ContainerTests.Sdk10Linux";
  public const string MultiSdkLinux = "ContainerTests.MultiSdkLinux";
}

public interface ISharedContainerRpcTarget
{
  Task<string?> PromptSelectionAsync(TestPromptSelectionRequest request);
  Task<string[]?> PromptMultiSelectionAsync(TestPromptSelectionRequest request);
  Task<string?> PromptStringAsync(TestPromptStringRequest request);
  Task<TestPickerResult?> PickerPickAsync(TestPickerRequest request);
  Task<bool> OpenBufferAsync(TestOpenBufferRequest request);
  Task<RunCommandResponse> RunCommandManagedAsync(TestTrackedJob job);
  void DisplayMessage(TestDisplayMessage message);
  void DisplayError(TestDisplayMessage message);
  void SetQuickFix(TestQuickFixItem[] quickFixItems);
  void SetQuickFixSilent(TestQuickFixItem[] quickFixItems);
  void SolutionProjectsLoaded(TestSolutionProjectsLoadedNotification notification);
  void RegisterTest(TestRegisterTestPayload payload);
  void RemoveTest(TestRemoveTestPayload payload);
  void UpdateStatus(TestUpdateStatusPayload payload);
  void UpdateStatusBatch(TestUpdateStatusBatchPayload payload);
  void TestrunnerStatusUpdate(RunnerStatusDto status);
  bool IsVisible();
}

public class ServerContainerFixture<TContainer> : IAsyncLifetime
  where TContainer : ServerContainer, new()
{
  private readonly SharedContainerRpcRouter _router = new();
  private readonly SemaphoreSlim _leaseLock = new(1, 1);
  private int _disposed;

  public TContainer Container { get; } = new();

  public ServerContainerFixture()
  {
    SharedContainerFixtureRegistry.Register(this);
  }

  public async Task InitializeAsync()
  {
    Container.RpcConfigurator = rpc =>
      rpc.AddLocalRpcTarget(_router, new JsonRpcTargetOptions { DisposeOnDisconnect = false });

    await Container.StartAsync();
  }

  public async Task<SharedContainerLease<TContainer>> LeaseAsync(ISharedContainerRpcTarget target)
  {
    await _leaseLock.WaitAsync();

    try
    {
      await ResetServerAsync();
      _router.ActiveTarget = target;
      return new SharedContainerLease<TContainer>(this);
    }
    catch
    {
      _leaseLock.Release();
      throw;
    }
  }

  internal async ValueTask ReleaseAsync()
  {
    try
    {
      await ResetServerAsync();
    }
    finally
    {
      _router.ActiveTarget = null;
      _leaseLock.Release();
    }
  }

  private Task ResetServerAsync() =>
    Container.Rpc.InvokeAsync("_server/test-reset").WaitAsync(TimeSpan.FromMinutes(1));

  public async Task DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
      return;

    SharedContainerFixtureRegistry.Unregister<TContainer>(this);
    await Container.DisposeAsync();
    _leaseLock.Dispose();
  }
}

public sealed class SharedContainerLease<TContainer> : IAsyncDisposable
  where TContainer : ServerContainer, new()
{
  private readonly ServerContainerFixture<TContainer> _fixture;
  private int _disposed;

  internal SharedContainerLease(ServerContainerFixture<TContainer> fixture) => _fixture = fixture;

  public TContainer Container => _fixture.Container;

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
      return;

    await _fixture.ReleaseAsync();
  }
}

public sealed class Sdk8LinuxFixture : ServerContainerFixture<Sdk8LinuxContainer>;
public sealed class Sdk9LinuxFixture : ServerContainerFixture<Sdk9LinuxContainer>;
public sealed class Sdk10LinuxFixture : ServerContainerFixture<Sdk10LinuxContainer>;
public sealed class MultiSdkLinuxFixture : ServerContainerFixture<MultiSdkLinuxContainer>;

[CollectionDefinition(ContainerCollections.Sdk8Linux)]
public sealed class Sdk8LinuxCollection : ICollectionFixture<Sdk8LinuxFixture>;

[CollectionDefinition(ContainerCollections.Sdk9Linux)]
public sealed class Sdk9LinuxCollection : ICollectionFixture<Sdk9LinuxFixture>;

[CollectionDefinition(ContainerCollections.Sdk10Linux)]
public sealed class Sdk10LinuxCollection : ICollectionFixture<Sdk10LinuxFixture>;

[CollectionDefinition(ContainerCollections.MultiSdkLinux)]
public sealed class MultiSdkLinuxCollection : ICollectionFixture<MultiSdkLinuxFixture>;

internal static class SharedContainerFixtureRegistry
{
  private static readonly object Gate = new();
  private static readonly Dictionary<Type, object> Fixtures = [];

  public static void Register<TContainer>(ServerContainerFixture<TContainer> fixture)
    where TContainer : ServerContainer, new()
  {
    lock (Gate)
    {
      Fixtures[typeof(TContainer)] = fixture;
    }
  }

  public static void Unregister<TContainer>(ServerContainerFixture<TContainer> fixture)
    where TContainer : ServerContainer, new()
  {
    lock (Gate)
    {
      if (Fixtures.TryGetValue(typeof(TContainer), out var current) && ReferenceEquals(current, fixture))
        Fixtures.Remove(typeof(TContainer));
    }
  }

  public static ServerContainerFixture<TContainer> Get<TContainer>()
    where TContainer : ServerContainer, new()
  {
    lock (Gate)
    {
      if (Fixtures.TryGetValue(typeof(TContainer), out var fixture))
        return (ServerContainerFixture<TContainer>)fixture;
    }

    throw new InvalidOperationException(
      $"No shared container fixture is registered for {typeof(TContainer).Name}. " +
      $"Add the test class to the matching xUnit collection.");
  }
}

public sealed record TestSolutionProjectsLoadedNotification();
public sealed record TestRegisterTestPayload(TestNodeDto Test);
public sealed record TestRemoveTestPayload(string Id);
public sealed record TestStatusDto(string Type);
public sealed record TestUpdateStatusPayload(string Id, TestStatusDto? Status, List<string>? AvailableActions);
public sealed record TestStatusUpdate(string Id, TestStatusDto? Status, List<string>? AvailableActions);
public sealed record TestUpdateStatusBatchPayload(List<TestStatusUpdate>? Updates);

internal sealed class SharedContainerRpcRouter
{
  public ISharedContainerRpcTarget? ActiveTarget { get; set; }

  private ISharedContainerRpcTarget RequireTarget() =>
    ActiveTarget ?? throw new InvalidOperationException("No active container test is registered for reverse RPC.");

  [JsonRpcMethod("promptSelection", UseSingleObjectParameterDeserialization = true)]
  public Task<string?> PromptSelection(TestPromptSelectionRequest request) =>
    RequireTarget().PromptSelectionAsync(request);

  [JsonRpcMethod("promptMultiSelection", UseSingleObjectParameterDeserialization = true)]
  public Task<string[]?> PromptMultiSelection(TestPromptSelectionRequest request) =>
    RequireTarget().PromptMultiSelectionAsync(request);

  [JsonRpcMethod("promptString", UseSingleObjectParameterDeserialization = true)]
  public Task<string?> PromptString(TestPromptStringRequest request) =>
    RequireTarget().PromptStringAsync(request);

  [JsonRpcMethod("picker/pick", UseSingleObjectParameterDeserialization = true)]
  public Task<TestPickerResult?> PickerPick(TestPickerRequest request) =>
    RequireTarget().PickerPickAsync(request);

  [JsonRpcMethod("openBuffer", UseSingleObjectParameterDeserialization = true)]
  public Task<bool> OpenBuffer(TestOpenBufferRequest request) =>
    RequireTarget().OpenBufferAsync(request);

  [JsonRpcMethod("runCommandManaged", UseSingleObjectParameterDeserialization = true)]
  public Task<RunCommandResponse> RunCommandManaged(TestTrackedJob job) =>
    RequireTarget().RunCommandManagedAsync(job);

  [JsonRpcMethod("displayMessage", UseSingleObjectParameterDeserialization = true)]
  public void DisplayMessage(TestDisplayMessage message) =>
    ActiveTarget?.DisplayMessage(message);

  [JsonRpcMethod("displayError", UseSingleObjectParameterDeserialization = true)]
  public void DisplayError(TestDisplayMessage message) =>
    ActiveTarget?.DisplayError(message);

  [JsonRpcMethod("quickfix/set")]
  public void SetQuickFix(TestQuickFixItem[] quickFixItems) =>
    ActiveTarget?.SetQuickFix(quickFixItems);

  [JsonRpcMethod("quickfix/set-silent")]
  public void SetQuickFixSilent(TestQuickFixItem[] quickFixItems) =>
    ActiveTarget?.SetQuickFixSilent(quickFixItems);

  [JsonRpcMethod("solution/projects-loaded", UseSingleObjectParameterDeserialization = true)]
  public void SolutionProjectsLoaded(TestSolutionProjectsLoadedNotification notification) =>
    ActiveTarget?.SolutionProjectsLoaded(notification);

  [JsonRpcMethod("registerTest", UseSingleObjectParameterDeserialization = true)]
  public void RegisterTest(TestRegisterTestPayload payload) =>
    ActiveTarget?.RegisterTest(payload);

  [JsonRpcMethod("removeTest", UseSingleObjectParameterDeserialization = true)]
  public void RemoveTest(TestRemoveTestPayload payload) =>
    ActiveTarget?.RemoveTest(payload);

  [JsonRpcMethod("updateStatus", UseSingleObjectParameterDeserialization = true)]
  public void UpdateStatus(TestUpdateStatusPayload payload) =>
    ActiveTarget?.UpdateStatus(payload);

  [JsonRpcMethod("updateStatusBatch", UseSingleObjectParameterDeserialization = true)]
  public void UpdateStatusBatch(TestUpdateStatusBatchPayload payload) =>
    ActiveTarget?.UpdateStatusBatch(payload);

  [JsonRpcMethod("testrunner/statusUpdate", UseSingleObjectParameterDeserialization = true)]
  public void TestrunnerStatusUpdate(RunnerStatusDto status) =>
    ActiveTarget?.TestrunnerStatusUpdate(status);

  [JsonRpcMethod("testrunner/isVisible")]
  public bool IsVisible() => ActiveTarget?.IsVisible() ?? true;
}
