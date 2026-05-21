using System.Text.Json;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using EasyDotnet.Debugger.Session;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Debugger.Tests.Dap;

public class ClientMessageInterceptorTests
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  [Test]
  public async Task EvaluateAssignmentResponseIncludesNumericVariablesReference()
  {
    var proxy = new FakeDebuggerProxy(new Response
    {
      Seq = 7,
      Type = "response",
      RequestSeq = 2,
      Success = true,
      Command = "setExpression",
      Body = JsonSerializer.SerializeToElement(new
      {
        value = "8",
        type = "int"
      }, SerializerOptions)
    });

    var interceptor = new ClientMessageInterceptor(
      NullLogger<ClientMessageInterceptor>.Instance,
      new ValueConverterService(
        NullLogger<ValueConverterService>.Instance,
        NullLoggerFactory.Instance),
      request => Task.FromResult(request),
      _ => { },
      () => { });

    var request = new Request
    {
      Seq = 1,
      Type = "request",
      Command = "evaluate",
      Arguments = JsonSerializer.SerializeToElement(new
      {
        expression = "x = 8",
        frameId = 10,
        context = "repl"
      }, SerializerOptions)
    };

    var passthrough = await interceptor.InterceptAsync(request, proxy, CancellationToken.None);

    await Assert.That(passthrough).IsNull();
    await Assert.That(proxy.ClientMessage).IsTypeOf<Response>();

    var response = (Response)proxy.ClientMessage!;
    await Assert.That(response.Command).IsEqualTo("evaluate");
    await Assert.That(response.RequestSeq).IsEqualTo(1001);
    await Assert.That(response.Success).IsTrue();
    await Assert.That(response.Body).IsNotNull();
    await Assert.That(response.Body!.Value.GetProperty("result").GetString()).IsEqualTo("8");
    await Assert.That(response.Body!.Value.GetProperty("variablesReference").GetInt32()).IsEqualTo(0);
  }

  private sealed class FakeDebuggerProxy(Response internalResponse) : IDebuggerProxy
  {
    public ProtocolMessage? ClientMessage { get; private set; }
    public Task Completion => Task.CompletedTask;

    public Task<Response> RunInternalRequestAsync(Request request, CancellationToken cancellationToken)
      => Task.FromResult(internalResponse);

    public Task<Response> RunClientRequestAsync(Request request, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task<VariablesResponse?> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public Task WriteProxyToClientAsync(ProtocolMessage response, CancellationToken cancellationToken)
    {
      ClientMessage = response;
      return Task.CompletedTask;
    }

    public Task EmitEventToClientAsync(Event evt, CancellationToken cancellationToken)
      => throw new NotImplementedException();

    public RequestContext? GetAndRemoveContext(int proxySeq)
      => new(
        RequestOrigin.Client,
        1001,
        new TaskCompletionSource<Response>(),
        CancellationToken.None);

    public int? PeekOriginalSeq(int proxySeq) => null;

    public void Start(CancellationToken cancellationToken, Action? onDisconnect = null)
      => throw new NotImplementedException();
  }
}