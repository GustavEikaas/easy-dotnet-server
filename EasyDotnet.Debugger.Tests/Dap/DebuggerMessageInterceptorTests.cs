using System.Text.Json;
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

  [Test]
  public async Task StackTraceResponseMapsSourcePathsWithoutChangingOtherPaths()
  {
    var mapper = CreateMapper();
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, mapper);
    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "stackTrace",
      Body = JsonSerializer.SerializeToElement(new
      {
        stackFrames = new[]
        {
          new
          {
            id = 1,
            source = new { name = "Program.cs", path = "Modeler/Program.cs" },
            line = 10
          }
        },
        metadata = new { path = "Modeler/metadata.json" }
      })
    };

    var result = (Response)(await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None))!;

    var body = result.Body!.Value;
    await Assert.That(body.GetProperty("stackFrames")[0].GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
    await Assert.That(body.GetProperty("metadata").GetProperty("path").GetString())
      .IsEqualTo("Modeler/metadata.json");
  }

  [Test]
  public async Task SetBreakpointsResponseMapsBreakpointSourcePath()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "setBreakpoints",
      Body = JsonSerializer.SerializeToElement(new
      {
        breakpoints = new[]
        {
          new { verified = true, source = new { name = "Program.cs", path = "Modeler/Program.cs" }, line = 10 }
        }
      })
    };

    var result = (Response)(await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None))!;

    await Assert.That(result.Body!.Value.GetProperty("breakpoints")[0].GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
  }

  [Test]
  public async Task BreakpointEventMapsBreakpointSourcePath()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "breakpoint",
      Body = JsonSerializer.SerializeToElement(new
      {
        reason = "changed",
        breakpoint = new { verified = true, source = new { name = "Program.cs", path = "Modeler/Program.cs" }, line = 10 }
      })
    };

    var result = (Event)(await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None))!;

    await Assert.That(result.Body!.Value.GetProperty("breakpoint").GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
  }

  [Test]
  public async Task LoadedSourcesResponseMapsDirectSourceArrayPaths()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "loadedSources",
      Body = JsonSerializer.SerializeToElement(new
      {
        sources = new[] { new { name = "Program.cs", path = "Modeler/Program.cs" } }
      })
    };

    var result = (Response)(await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None))!;

    await Assert.That(result.Body!.Value.GetProperty("sources")[0].GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
  }

  [Test]
  public async Task OutputEventDoesNotMapSourcePathsInsideOpaqueData()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "output",
      Body = JsonSerializer.SerializeToElement(new
      {
        output = "diagnostic",
        source = new { name = "Program.cs", path = "Modeler/Program.cs" },
        data = new
        {
          source = new { path = "Modeler/opaque.cs" }
        }
      })
    };

    var result = (Event)(await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None))!;
    var body = result.Body!.Value;

    await Assert.That(body.GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
    await Assert.That(body.GetProperty("data").GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("Modeler/opaque.cs");
  }

  [Test]
  public async Task SourceAdapterDataRemainsOpaque()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "loadedSource",
      Body = JsonSerializer.SerializeToElement(new
      {
        reason = "new",
        source = new
        {
          path = "Modeler/Program.cs",
          adapterData = new
          {
            source = new { path = "Modeler/opaque.cs" }
          }
        }
      })
    };

    var result = (Event)(await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None))!;
    var source = result.Body!.Value.GetProperty("source");

    await Assert.That(source.GetProperty("path").GetString()).IsEqualTo("/work/modeler/Program.cs");
    await Assert.That(source.GetProperty("adapterData").GetProperty("source").GetProperty("path").GetString())
      .IsEqualTo("Modeler/opaque.cs");
  }

  [Test]
  public async Task NestedOfficialSourcesAreMapped()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var evt = new Event
    {
      Seq = 10,
      Type = "event",
      EventName = "loadedSource",
      Body = JsonSerializer.SerializeToElement(new
      {
        reason = "new",
        source = new
        {
          path = "Modeler/Program.cs",
          sources = new[] { new { path = "Modeler/Generated.cs" } }
        }
      })
    };

    var result = (Event)(await interceptor.InterceptAsync(evt, new FakeDebuggerProxy(), CancellationToken.None))!;
    var source = result.Body!.Value.GetProperty("source");

    await Assert.That(source.GetProperty("path").GetString()).IsEqualTo("/work/modeler/Program.cs");
    await Assert.That(source.GetProperty("sources")[0].GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Generated.cs");
  }

  [Test]
  public async Task DisassembleResponseMapsInstructionLocationsOnly()
  {
    var interceptor = CreateInterceptor(_ => { }, () => { }, (_, _) => { }, CreateMapper());
    var response = new Response
    {
      Seq = 10,
      Type = "response",
      RequestSeq = 1,
      Success = true,
      Command = "disassemble",
      Body = JsonSerializer.SerializeToElement(new
      {
        instructions = new[]
        {
          new
          {
            address = "0x1",
            instruction = "nop",
            location = new { path = "Modeler/Program.cs" }
          }
        },
        metadata = new
        {
          location = new { path = "Modeler/opaque.cs" }
        }
      })
    };

    var result = (Response)(await interceptor.InterceptAsync(response, new FakeDebuggerProxy(), CancellationToken.None))!;
    var body = result.Body!.Value;

    await Assert.That(body.GetProperty("instructions")[0].GetProperty("location").GetProperty("path").GetString())
      .IsEqualTo("/work/modeler/Program.cs");
    await Assert.That(body.GetProperty("metadata").GetProperty("location").GetProperty("path").GetString())
      .IsEqualTo("Modeler/opaque.cs");
  }

  private static DebuggerMessageInterceptor CreateInterceptor(
    Action<string> onDebugStartSignal,
    Action onDebuggerConfigurationDone,
    Action<string, string?> onDebugSessionStartFailed,
    SourcePathMapper? sourcePathMapper = null) =>
    new(
      NullLogger<DebuggerMessageInterceptor>.Instance,
      new ValueConverterService(
        NullLogger<ValueConverterService>.Instance,
        NullLoggerFactory.Instance),
      applyValueConverters: false,
      _ => { },
      onDebugStartSignal,
      onDebuggerConfigurationDone,
      onDebugSessionStartFailed,
      sourcePathMapper: sourcePathMapper);

  private static SourcePathMapper CreateMapper()
  {
    var mapper = new SourcePathMapper();
    mapper.Configure(new Dictionary<string, string> { ["Modeler"] = "/work/modeler" });
    return mapper;
  }

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