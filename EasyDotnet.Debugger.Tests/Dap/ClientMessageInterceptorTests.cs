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

    var interceptor = CreateInterceptor();

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

  [Test]
  public async Task AttachRequestIsDeserializedAndRewritten()
  {
    var rewriterCalled = false;
    var interceptor = CreateInterceptor(attachRequestRewriter: request =>
    {
      rewriterCalled = true;
      request.Arguments.Cwd = "/rewritten";
      return Task.FromResult(request);
    });
    var request = DapMessageDeserializer.Parse("""
      {
        "seq": 1,
        "type": "request",
        "command": "attach",
        "arguments": {
          "processId": 123,
          "customOption": true
        }
      }
      """);

    var result = await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None);

    await Assert.That(rewriterCalled).IsTrue();
    await Assert.That(result).IsTypeOf<InterceptableAttachRequest>();
    var attachRequest = (InterceptableAttachRequest)result!;
    await Assert.That(attachRequest.Arguments.ProcessId).IsEqualTo(123);
    await Assert.That(attachRequest.Arguments.Cwd).IsEqualTo("/rewritten");
    await Assert.That(attachRequest.Arguments.Other["customOption"].GetBoolean()).IsTrue();
  }

  [Test]
  public async Task ConfiguredSourcePathMapIsAppliedToSourceBearingGenericRequests()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string> { ["Modeler"] = "/work/modeler" });
    var interceptor = CreateInterceptor(mapper);
    var request = new Request
    {
      Seq = 2,
      Type = "request",
      Command = "breakpointLocations",
      Arguments = JsonSerializer.SerializeToElement(new
      {
        source = new { name = "Program.cs", path = "/work/modeler/Program.cs" },
        line = 10,
        metadata = new { path = "/work/modeler/metadata.json" }
      })
    };

    var result = (Request)(await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None))!;

    var arguments = result.Arguments!.Value;
    await Assert.That(arguments.GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("Modeler/Program.cs");
    await Assert.That(arguments.GetProperty("metadata").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/metadata.json");
  }

  [Test]
  public async Task EvaluateRequestMapsAdapterExtensionSourceBeforeSpecializedHandling()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string> { ["Modeler"] = "/work/modeler" });
    var interceptor = CreateInterceptor(mapper);
    var request = new Request
    {
      Seq = 2,
      Type = "request",
      Command = "evaluate",
      Arguments = JsonSerializer.SerializeToElement(new
      {
        expression = "value",
        context = "repl",
        source = new { path = "/work/modeler/Program.cs" }
      })
    };

    var result = (Request)(await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None))!;

    await Assert.That(result.Arguments!.Value.GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("Modeler/Program.cs");
  }

  [Test]
  public async Task MinimalSetBreakpointsRequestMapsSourcePath()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string> { ["Modeler"] = "/work/modeler" });
    var interceptor = CreateInterceptor(mapper);
    var request = DapMessageDeserializer.Parse("""
      {
        "seq": 2,
        "type": "request",
        "command": "setBreakpoints",
        "arguments": {
          "source": {
            "path": "/work/modeler/Program.cs"
          }
        }
      }
      """);

    var result = (SetBreakpointsRequest)(await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None))!;

    await Assert.That(result.Arguments.Source.Path).IsEqualTo("Modeler/Program.cs");
    await Assert.That(result.Arguments.Breakpoints).IsNull();
    await Assert.That(result.Arguments.Lines).IsNull();
    await Assert.That(result.Arguments.SourceModified).IsNull();
  }

  [Test]
  public async Task SourceReferenceOnlySetBreakpointsRequestPreservesUnknownSourceFields()
  {
    var interceptor = CreateInterceptor(new SourcePathMapper());
    var request = DapMessageDeserializer.Parse("""
      {
        "seq": 2,
        "type": "request",
        "command": "setBreakpoints",
        "arguments": {
          "source": {
            "sourceReference": 42,
            "adapterData": {
              "documentId": "generated"
            }
          }
        }
      }
      """);

    var result = (SetBreakpointsRequest)(await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None))!;
    var serialized = JsonSerializer.SerializeToElement(result, result.GetType(), SerializerOptions);
    var source = serialized.GetProperty("arguments").GetProperty("source");

    await Assert.That(result.Arguments.Source.Path).IsNull();
    await Assert.That(source.GetProperty("sourceReference").GetInt32()).IsEqualTo(42);
    await Assert.That(source.GetProperty("adapterData").GetProperty("documentId").GetString()).IsEqualTo("generated");
  }

  [Test]
  public async Task SetBreakpointsRequestMapsOfficialSourceTreeAndPreservesAdapterData()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string> { ["Modeler"] = "/work/modeler" });
    var interceptor = CreateInterceptor(mapper);
    var request = DapMessageDeserializer.Parse("""
      {
        "seq": 2,
        "type": "request",
        "command": "setBreakpoints",
        "arguments": {
          "source": {
            "path": "/work/modeler/Program.cs",
            "sources": [
              {
                "path": "/work/modeler/Generated.cs",
                "adapterData": {
                  "source": {
                    "path": "/work/modeler/nested-opaque.cs"
                  }
                }
              }
            ],
            "adapterData": {
              "source": {
                "path": "/work/modeler/opaque.cs"
              }
            }
          }
        }
      }
      """);

    var result = (SetBreakpointsRequest)(await interceptor.InterceptAsync(request, new FakeDebuggerProxy(), CancellationToken.None))!;
    var serialized = JsonSerializer.SerializeToElement(result, result.GetType(), SerializerOptions);
    var source = serialized.GetProperty("arguments").GetProperty("source");

    await Assert.That(source.GetProperty("path").GetString()).IsEqualTo("Modeler/Program.cs");
    await Assert.That(source.GetProperty("sources")[0].GetProperty("path").GetString())
      .IsEqualTo("Modeler/Generated.cs");
    await Assert.That(source.GetProperty("adapterData").GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/opaque.cs");
    await Assert.That(source.GetProperty("sources")[0].GetProperty("adapterData").GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/nested-opaque.cs");
  }

  private static ClientMessageInterceptor CreateInterceptor(
    SourcePathMapper? sourcePathMapper = null,
    Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>>? attachRequestRewriter = null) => new(
    NullLogger<ClientMessageInterceptor>.Instance,
    new ValueConverterService(
      NullLogger<ValueConverterService>.Instance,
      NullLoggerFactory.Instance),
    attachRequestRewriter ?? (request => Task.FromResult(request)),
    _ => { },
    () => { },
    sourcePathMapper: sourcePathMapper);

  private sealed class FakeDebuggerProxy(Response? internalResponse = null) : IDebuggerProxy
  {
    public ProtocolMessage? ClientMessage { get; private set; }
    public Task Completion => Task.CompletedTask;

    public Task<Response> RunInternalRequestAsync(Request request, CancellationToken cancellationToken)
      => Task.FromResult(internalResponse ?? throw new InvalidOperationException("No internal response was configured."));

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