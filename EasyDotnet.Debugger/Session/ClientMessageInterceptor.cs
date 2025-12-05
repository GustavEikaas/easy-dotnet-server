using System.Text.Json;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class ClientMessageInterceptor(
  ILogger<ClientMessageInterceptor> logger,
  ValueConverterService valueConverterService,
  Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> attachRequestRewriter) : IDapMessageInterceptor
{
  private static readonly JsonSerializerOptions LoggingOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

  public async Task<ProtocolMessage?> InterceptAsync(
    ProtocolMessage message,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    try
    {
      return message switch
      {
        InterceptableAttachRequest attachReq => await HandleAttachRequestAsync(attachReq),
        InterceptableVariablesRequest varReq => await HandleVariablesRequestAsync(varReq, proxy, cancellationToken),
        SetBreakpointsRequest bpReq => HandleBreakpointsRequest(bpReq),
        Request req => LogAndPassthrough(req),
        _ => throw new Exception($"Unsupported DAP message from client: {message}")
      };
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Exception in client DAP handler");
      throw;
    }
  }

  private async Task<ProtocolMessage> HandleAttachRequestAsync(InterceptableAttachRequest request)
  {
    var modified = await attachRequestRewriter(request);
    logger.LogInformation("[CLIENT] Attach request: {request}",
      JsonSerializer.Serialize(modified, LoggingOptions));
    return modified;
  }

  private async Task<ProtocolMessage?> HandleVariablesRequestAsync(
    InterceptableVariablesRequest request,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    if (request.Arguments?.VariablesReference is not null)
    {
      var converter = valueConverterService.TryGetConverterFor(request.Arguments.VariablesReference);
      if (converter is not null)
      {
        var result = await converter.TryConvertAsync(
          request.Arguments.VariablesReference,
          proxy,
          cancellationToken);

        var context = proxy.GetAndRemoveContext(request.Seq)
          ?? throw new Exception("Proxy request not found");

        result.RequestSeq = context.OriginalSeq;
        await proxy.WriteProxyToClientAsync(result, cancellationToken);
        return null;
      }
    }

    logger.LogDebug("[CLIENT] Variables request: {request}",
      JsonSerializer.Serialize(request, LoggingOptions));
    return request;
  }

  private SetBreakpointsRequest HandleBreakpointsRequest(SetBreakpointsRequest request)
  {
    if (OperatingSystem.IsWindows())
    {
      request.Arguments.Source.Path = request.Arguments.Source.Path.Replace('/', '\\');
      logger.LogDebug("[CLIENT] Normalized breakpoint path separators");
    }

    logger.LogDebug("[CLIENT] Set breakpoints: {request}",
      JsonSerializer.Serialize(request, LoggingOptions));
    return request;
  }

  private Request LogAndPassthrough(Request request)
  {
    logger.LogDebug("[CLIENT] Request: {command}", request.Command);
    return request;
  }
}