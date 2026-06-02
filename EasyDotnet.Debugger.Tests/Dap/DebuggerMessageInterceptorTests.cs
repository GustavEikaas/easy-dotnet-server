using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Debugger.Tests.Dap;

public class DebuggerMessageInterceptorTests
{
  [Test]
  public async Task SuccessfulAttachResponseReportsStartSignal()
  {
    var startSignals = new List<string>();
    var configurationDoneCount = 0;
    var failures = new List<string>();
    var interceptor = CreateInterceptor(
      signal => startSignals.Add(signal),
      () => configurationDoneCount++,
      (command, message) => failures.Add($"{command}:{message}"));

    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "attach"
    };

    var result = await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(response);
    await Assert.That(startSignals).Contains("attach response");
    await Assert.That(configurationDoneCount).IsEqualTo(0);
    await Assert.That(failures.Count).IsEqualTo(0);
  }

  [Test]
  public async Task SuccessfulConfigurationDoneResponseReportsConfigurationDone()
  {
    var startSignals = new List<string>();
    var configurationDoneCount = 0;
    var failures = new List<string>();
    var interceptor = CreateInterceptor(
      signal => startSignals.Add(signal),
      () => configurationDoneCount++,
      (command, message) => failures.Add($"{command}:{message}"));

    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "configurationDone"
    };

    var result = await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(response);
    await Assert.That(startSignals.Count).IsEqualTo(0);
    await Assert.That(configurationDoneCount).IsEqualTo(1);
    await Assert.That(failures.Count).IsEqualTo(0);
  }

  [Test]
  public async Task FailedConfigurationDoneResponseReportsDebugSessionStartFailure()
  {
    var startSignals = new List<string>();
    var configurationDoneCount = 0;
    var failures = new List<string>();
    var interceptor = CreateInterceptor(
      signal => startSignals.Add(signal),
      () => configurationDoneCount++,
      (command, message) => failures.Add($"{command}:{message}"));

    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = false,
      Command = "configurationDone",
      Message = "attach failed"
    };

    var result = await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(response);
    await Assert.That(startSignals.Count).IsEqualTo(0);
    await Assert.That(configurationDoneCount).IsEqualTo(0);
    await Assert.That(failures).Contains("configurationDone:attach failed");
  }

  [Test]
  public async Task ProcessEventReportsStartSignal()
  {
    var startSignals = new List<string>();
    var interceptor = CreateInterceptor(signal => startSignals.Add(signal), () => { }, (_, _) => { });

    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "process"
    };

    var result = await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(evt);
    await Assert.That(startSignals).Contains("process event");
  }

  private static DebuggerMessageInterceptor CreateInterceptor(
    Action<string> onDebugStartSignal,
    Action onDebuggerConfigurationDone,
    Action<string, string?> onDebugSessionStartFailed) =>
    new(
      NullLogger<DebuggerMessageInterceptor>.Instance,
      new ValueConverterService(
        NullLogger<ValueConverterService>.Instance,
        NullLoggerFactory.Instance),
      applyValueConverters: false,
      _ => { },
      onDebugStartSignal,
      onDebuggerConfigurationDone,
      onDebugSessionStartFailed);

  private sealed class FakeDebuggerProxy : IDebuggerProxy
  {
    public Task Completion => Task.CompletedTask;

    public void Start(CancellationToken cancellationToken, Action? onDisconnect = null)
      => throw new NotImplementedException();

    public Task<Response> RunInternalRequestAsync(Request request, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task<Response> RunClientRequestAsync(Request request, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task<VariablesResponse?> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task WriteProxyToClientAsync(ProtocolMessage response, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task EmitEventToClientAsync(Event evt, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public RequestContext? GetAndRemoveContext(int proxySeq)
      => throw new NotImplementedException();

    public int? PeekOriginalSeq(int proxySeq)
      => throw new NotImplementedException();
  }
}