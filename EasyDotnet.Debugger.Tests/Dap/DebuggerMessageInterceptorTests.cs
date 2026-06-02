using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Debugger.Tests.Dap;

public class DebuggerMessageInterceptorTests
{
  [Test]
  public async Task SuccessfulAttachResponseReportsDebugSessionStarted()
  {
    var startedSignals = new List<string>();
    var failures = new List<string>();
    var interceptor = CreateInterceptor(
      signal => startedSignals.Add(signal),
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
    await Assert.That(startedSignals).Contains("attach response");
    await Assert.That(failures.Count).IsEqualTo(0);
  }

  [Test]
  public async Task FailedAttachResponseReportsDebugSessionStartFailure()
  {
    var startedSignals = new List<string>();
    var failures = new List<string>();
    var interceptor = CreateInterceptor(
      signal => startedSignals.Add(signal),
      (command, message) => failures.Add($"{command}:{message}"));

    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = false,
      Command = "attach",
      Message = "attach failed"
    };

    var result = await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(response);
    await Assert.That(startedSignals.Count).IsEqualTo(0);
    await Assert.That(failures).Contains("attach:attach failed");
  }

  [Test]
  public async Task ProcessEventReportsDebugSessionStarted()
  {
    var startedSignals = new List<string>();
    var interceptor = CreateInterceptor(signal => startedSignals.Add(signal), (_, _) => { });

    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "process"
    };

    var result = await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(result).IsSameReferenceAs(evt);
    await Assert.That(startedSignals).Contains("process event");
  }

  private static DebuggerMessageInterceptor CreateInterceptor(
    Action<string> onDebugSessionStarted,
    Action<string, string?> onDebugSessionStartFailed) =>
    new(
      NullLogger<DebuggerMessageInterceptor>.Instance,
      new ValueConverterService(
        NullLogger<ValueConverterService>.Instance,
        NullLoggerFactory.Instance),
      applyValueConverters: false,
      _ => { },
      onDebugSessionStarted,
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
