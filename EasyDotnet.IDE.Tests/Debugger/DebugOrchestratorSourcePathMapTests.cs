using System.Text.Json;
using EasyDotnet.BuildServer.Contracts;
using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Project.Services;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Types;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.IDE.Tests.Debugger;

public sealed class DebugOrchestratorSourcePathMapTests
{
  [Test]
  public async Task StartClientDebugSessionAwaitsExactGraphResultAndPassesMappingsToFactory()
  {
    var graph = new ControllableProjectGraphService();
    var factory = new CapturingDebugSessionFactory();
    var orchestrator = CreateOrchestrator(graph, factory);

    var start = orchestrator.StartClientDebugSessionAsync(
        "App.csproj",
        new StubDebugSessionStrategy(),
        CancellationToken.None);

    await Assert.That(start.IsCompleted).IsFalse();
    await Assert.That(factory.AutomaticSourceFileMap).IsNull();

    graph.Complete(CreateSnapshot("/work/App=Mapped/App"));

    await Assert.ThrowsAsync<FactoryCalledException>(async () => await start);
    await Assert.That(factory.AutomaticSourceFileMap).IsNotNull();
    await Assert.That(factory.AutomaticSourceFileMap!["Mapped/App"]).IsEqualTo("/work/App");
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task StartClientDebugSessionContinuesWithEmptyMappingsWithoutGraphOrAfterGraphFailure(
      bool graphFails)
  {
    var graph = new ControllableProjectGraphService();
    if (graphFails)
    {
      graph.Fail(new InvalidOperationException("Graph evaluation failed"));
    }
    else
    {
      graph.Complete(null);
    }
    var factory = new CapturingDebugSessionFactory();
    var orchestrator = CreateOrchestrator(graph, factory);

    var start = orchestrator.StartClientDebugSessionAsync(
        "App.csproj",
        new StubDebugSessionStrategy(),
        CancellationToken.None);

    await Assert.ThrowsAsync<FactoryCalledException>(async () => await start);
    await Assert.That(factory.AutomaticSourceFileMap).IsEmpty();
  }

  [Test]
  public async Task StartClientDebugSessionContinuesWithEmptyMappingsWhenGraphWaitTimesOut()
  {
    var graph = new ControllableProjectGraphService();
    var factory = new CapturingDebugSessionFactory();
    var orchestrator = CreateOrchestrator(graph, factory, TimeSpan.Zero);

    var start = orchestrator.StartClientDebugSessionAsync(
        "App.csproj",
        new StubDebugSessionStrategy(),
        CancellationToken.None);

    await Assert.ThrowsAsync<FactoryCalledException>(async () => await start);
    await Assert.That(factory.AutomaticSourceFileMap).IsEmpty();
  }

  [Test]
  public async Task StartClientDebugSessionPropagatesCallerCancellationWhileWaitingForGraph()
  {
    var graph = new ControllableProjectGraphService();
    var factory = new CapturingDebugSessionFactory();
    var orchestrator = CreateOrchestrator(graph, factory, TimeSpan.FromMinutes(1));
    using var cancellation = new CancellationTokenSource();

    var start = orchestrator.StartClientDebugSessionAsync(
        "App.csproj",
        new StubDebugSessionStrategy(),
        cancellation.Token);
    cancellation.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(async () => await start);
    await Assert.That(factory.AutomaticSourceFileMap).IsNull();
  }

  private static DebugOrchestrator CreateOrchestrator(
      IProjectGraphService projectGraphService,
      CapturingDebugSessionFactory debugSessionFactory,
      TimeSpan? automaticSourceFileMapWaitTimeout = null) => new(
    new InvokingDebugSessionManager(),
    debugSessionFactory,
    editorService: null!,
    new ClientService
    {
      ClientOptions = new ClientOptions(new DebuggerOptions(
          BinaryPath: "/tmp/debugger",
          Engine: "netcoredbg"))
    },
    variableLocationResolver: null!,
    projectGraphService,
    new MsBuildSourcePathMapProvider(NullLogger<MsBuildSourcePathMapProvider>.Instance),
    NullLogger<DebugOrchestrator>.Instance,
    automaticSourceFileMapWaitTimeout);

  private static ProjectGraphSnapshot CreateSnapshot(string pathMap)
  {
    var raw = JsonSerializer.Deserialize<DotnetProject>(JsonSerializer.Serialize(new { PathMap = pathMap }))!;
    var project = new ValidatedDotnetProject
    {
      TargetFramework = "net8.0",
      OutputType = "Library",
      ProjectPath = "App.csproj",
      ProjectFullPath = "/work/App/App.csproj",
      ProjectName = "App",
      AssemblyName = "App",
      TargetPath = "/work/App/App.dll",
      Raw = raw
    };
    return new ProjectGraphSnapshot("App.sln", [], [project], []);
  }

  private sealed class ControllableProjectGraphService : IProjectGraphService
  {
    private readonly TaskCompletionSource<ProjectGraphSnapshot?> _load =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ProjectGraphSnapshot?> LoadSolutionAsync(string solutionFile, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public Task<ProjectGraphSnapshot?> WaitForCurrentLoadAsync(CancellationToken ct = default) =>
      _load.Task.WaitAsync(ct);

    public void Complete(ProjectGraphSnapshot? snapshot) => _load.SetResult(snapshot);

    public void Fail(Exception exception) => _load.SetException(exception);
  }

  private sealed class CapturingDebugSessionFactory : IDebugSessionFactory
  {
    public IReadOnlyDictionary<string, string>? AutomaticSourceFileMap { get; private set; }

    public EasyDotnet.Debugger.DebugSession Create(
        Func<InterceptableAttachRequest, IDebuggerProxy, Task<InterceptableAttachRequest>> attachRequestRewriter,
        bool applyValueConverters,
        bool memCpuUsage,
        IVariableLocationResolver? variableLocationResolver = null,
        IReadOnlyDictionary<string, string>? automaticSourceFileMap = null)
    {
      AutomaticSourceFileMap = automaticSourceFileMap is null
        ? null
        : new Dictionary<string, string>(automaticSourceFileMap);
      throw new FactoryCalledException();
    }
  }

  private sealed class InvokingDebugSessionManager : IDebugSessionManager
  {
    public Task<EasyDotnet.Debugger.DebugSession> StartServerSessionAsync(
        string projectPath,
        string sessionId,
        Func<Task<EasyDotnet.Debugger.DebugSession>> sessionFactory,
        CancellationToken cancellationToken) => sessionFactory();

    public Task<EasyDotnet.Debugger.DebugSession> StartClientSessionAsync(
        string projectPath,
        Func<Task<EasyDotnet.Debugger.DebugSession>> sessionFactory,
        CancellationToken cancellationToken) => sessionFactory();

    public Task EndSessionAsync(string projectPath, CancellationToken cancellationToken) =>
      throw new NotSupportedException();

    public EasyDotnet.IDE.Services.DebugSession? GetSession(string projectPath) => null;

    public bool HasActiveSession(string projectPath) => false;
  }

  private sealed class StubDebugSessionStrategy : IDebugSessionStrategy
  {
    public Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

    public Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy) =>
      Task.CompletedTask;

    public Task<int>? GetProcessIdAsync() => null;

    public void OnDebugSessionReady(EasyDotnet.Debugger.DebugSession debugSession, IDebuggerProxy proxy) { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class FactoryCalledException : Exception;
}