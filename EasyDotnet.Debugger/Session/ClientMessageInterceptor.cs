using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Debugger.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Debugger.Services;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Debugger.Session;

public class ClientMessageInterceptor(
  ILogger<ClientMessageInterceptor> logger,
  ValueConverterService valueConverterService,
  Func<InterceptableAttachRequest, Task<InterceptableAttachRequest>> attachRequestRewriter,
  Action<int> onDebugeeProcessStarted,
  Action onConfigurationDone,
  FrameSourceTracker? frameSourceTracker = null
  ) : IDapMessageInterceptor
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
        InterceptableCompletionsRequest complReq => await HandleCompletionsRequestAsync(complReq, proxy, cancellationToken),
        ScopesRequest scopesReq => HandleScopesRequest(scopesReq, proxy),
        SetBreakpointsRequest bpReq => HandleBreakpointsRequest(bpReq),
        Request req when req.Command == "evaluate" => await HandleEvaluateRequestAsync(req, proxy, cancellationToken),
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
    var processId = modified.Arguments.ProcessId;
    if (processId is not null)
    {
      onDebugeeProcessStarted?.Invoke(processId.Value);
    }
    return modified;
  }

  private async Task<ProtocolMessage?> HandleVariablesRequestAsync(
    InterceptableVariablesRequest request,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    if (request.Arguments?.VariablesReference is not null)
    {
      TrackVariablesRequest(request, proxy);
      var converter = valueConverterService.TryGetConverterFor(request.Arguments.VariablesReference);
      if (converter is not null)
      {
        var result = await converter.TryConvertAsync(
          request.Arguments.VariablesReference,
          proxy,
          cancellationToken);

        valueConverterService.RegisterVariablesReferences(result);

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

  private static readonly JsonSerializerOptions ProxyRequestOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
  };

  private async Task<ProtocolMessage?> HandleCompletionsRequestAsync(
    InterceptableCompletionsRequest request,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    var context = proxy.GetAndRemoveContext(request.Seq)
      ?? throw new Exception("Proxy request not found for completions");

    try
    {
      var args = request.Arguments ?? new InterceptableCompletionsArguments();
      var parsed = ParseInput(args.Text ?? string.Empty, args.Column);

      List<CompletionItem> targets;
      if (args.FrameId is null)
      {
        targets = [];
      }
      else if (parsed.ParentExpression is { } parent)
      {
        targets = await GatherMembersAsync(args.FrameId.Value, parent, parsed.Prefix, parsed.StartColumn, parsed.Length, proxy, cancellationToken);
      }
      else
      {
        targets = await GatherFrameLocalsAsync(args.FrameId.Value, parsed.Prefix, proxy, cancellationToken);
      }

      var response = new CompletionsResponse
      {
        Seq = 0,
        Type = "response",
        RequestSeq = context.OriginalSeq,
        Success = true,
        Command = "completions",
        Body = new CompletionsResponseBody { Targets = targets }
      };

      await proxy.WriteProxyToClientAsync(response, cancellationToken);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[CLIENT] completions failed");
      await proxy.WriteProxyToClientAsync(new CompletionsResponse
      {
        Seq = 0,
        Type = "response",
        RequestSeq = context.OriginalSeq,
        Success = true,
        Command = "completions",
        Body = new CompletionsResponseBody { Targets = [] }
      }, cancellationToken);
    }

    return null;
  }

  private readonly record struct ParsedInput(string? ParentExpression, string Prefix, int StartColumn, int Length);

  private static ParsedInput ParseInput(string text, int column)
  {
    if (string.IsNullOrEmpty(text))
    {
      return new ParsedInput(null, string.Empty, column, 0);
    }

    var cursor = Math.Clamp(column - 1, 0, text.Length);
    var prefixStart = cursor;
    while (prefixStart > 0 && IsIdentifierChar(text[prefixStart - 1]))
    {
      prefixStart--;
    }

    var prefix = text[prefixStart..cursor];
    var startCol = prefixStart + 1;
    var length = prefix.Length;

    if (prefixStart > 0 && text[prefixStart - 1] == '.')
    {
      var parentEnd = prefixStart - 1;
      var parentStart = parentEnd;
      while (parentStart > 0 && IsParentChar(text[parentStart - 1]))
      {
        parentStart--;
      }

      if (parentStart < parentEnd)
      {
        var parent = text[parentStart..parentEnd];
        if (IsSafeParentExpression(parent))
        {
          return new ParsedInput(parent, prefix, startCol, length);
        }
      }
    }

    return new ParsedInput(null, prefix, startCol, length);
  }

  private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
  private static bool IsParentChar(char c) => IsIdentifierChar(c) || c == '.';

  private static bool IsSafeParentExpression(string expr)
  {
    if (string.IsNullOrEmpty(expr) || expr.StartsWith('.') || expr.EndsWith('.') || expr.Contains(".."))
    {
      return false;
    }

    foreach (var part in expr.Split('.'))
    {
      if (part.Length == 0 || !(part[0] == '_' || char.IsLetter(part[0])))
      {
        return false;
      }
      foreach (var c in part)
      {
        if (!IsIdentifierChar(c))
        {
          return false;
        }
      }
    }

    return true;
  }

  private async Task<List<CompletionItem>> GatherFrameLocalsAsync(
    int frameId,
    string prefix,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    var scopesReq = new Request
    {
      Seq = 0,
      Type = "request",
      Command = "scopes",
      Arguments = JsonSerializer.SerializeToElement(new { frameId }, ProxyRequestOptions)
    };

    var scopesResp = await proxy.RunInternalRequestAsync(scopesReq, cancellationToken);
    if (!scopesResp.Success || scopesResp.Body is not { ValueKind: JsonValueKind.Object } body
        || !body.TryGetProperty("scopes", out var scopesElement)
        || scopesElement.ValueKind != JsonValueKind.Array)
    {
      return [];
    }

    var results = new List<CompletionItem>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var scope in scopesElement.EnumerateArray())
    {
      if (!scope.TryGetProperty("variablesReference", out var refEl) || refEl.ValueKind != JsonValueKind.Number)
      {
        continue;
      }

      var varsResp = await proxy.GetVariablesAsync(refEl.GetInt32(), cancellationToken);
      if (varsResp?.Body?.Variables is null)
      {
        continue;
      }

      foreach (var v in varsResp.Body.Variables)
      {
        if (string.IsNullOrEmpty(v.Name) || !seen.Add(v.Name))
        {
          continue;
        }

        if (prefix.Length > 0 && !v.Name.StartsWith(prefix, StringComparison.Ordinal))
        {
          continue;
        }

        results.Add(new CompletionItem
        {
          Label = v.Name,
          Type = "variable",
          Detail = string.IsNullOrEmpty(v.Type) ? null : v.Type
        });
      }
    }

    return results;
  }

  private async Task<List<CompletionItem>> GatherMembersAsync(
    int frameId,
    string parentExpression,
    string prefix,
    int startColumn,
    int length,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    var evalReq = new Request
    {
      Seq = 0,
      Type = "request",
      Command = "evaluate",
      Arguments = JsonSerializer.SerializeToElement(new
      {
        expression = parentExpression,
        frameId,
        context = "repl"
      }, ProxyRequestOptions)
    };

    var evalResp = await proxy.RunInternalRequestAsync(evalReq, cancellationToken);
    if (!evalResp.Success || evalResp.Body is not { ValueKind: JsonValueKind.Object } body
        || !body.TryGetProperty("variablesReference", out var refEl)
        || refEl.ValueKind != JsonValueKind.Number)
    {
      return [];
    }

    var varsRef = refEl.GetInt32();
    if (varsRef <= 0)
    {
      return [];
    }

    var varsResp = await proxy.GetVariablesAsync(varsRef, cancellationToken);
    if (varsResp?.Body?.Variables is null)
    {
      return [];
    }

    var results = new List<CompletionItem>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var v in varsResp.Body.Variables)
    {
      if (string.IsNullOrEmpty(v.Name) || !seen.Add(v.Name))
      {
        continue;
      }

      if (prefix.Length > 0 && !v.Name.StartsWith(prefix, StringComparison.Ordinal))
      {
        continue;
      }

      results.Add(new CompletionItem
      {
        Label = v.Name,
        Type = "property",
        Start = startColumn,
        Length = length,
        Detail = string.IsNullOrEmpty(v.Type) ? null : v.Type
      });
    }

    return results;
  }

  private async Task<ProtocolMessage?> HandleEvaluateRequestAsync(
    Request request,
    IDebuggerProxy proxy,
    CancellationToken cancellationToken)
  {
    if (request.Arguments is not { ValueKind: JsonValueKind.Object } args)
    {
      return LogAndPassthrough(request);
    }

    var contextKind = args.TryGetProperty("context", out var ctxEl) && ctxEl.ValueKind == JsonValueKind.String
      ? ctxEl.GetString()
      : null;
    if (contextKind != "repl")
    {
      return LogAndPassthrough(request);
    }

    var expression = args.TryGetProperty("expression", out var exprEl) && exprEl.ValueKind == JsonValueKind.String
      ? exprEl.GetString()
      : null;
    if (string.IsNullOrEmpty(expression))
    {
      return LogAndPassthrough(request);
    }

    var parsed = TryParseAssignment(expression);
    if (parsed is null)
    {
      return LogAndPassthrough(request);
    }

    var (lhs, rhs) = parsed.Value;
    int? frameId = args.TryGetProperty("frameId", out var frameEl) && frameEl.ValueKind == JsonValueKind.Number
      ? frameEl.GetInt32()
      : null;

    var context = proxy.GetAndRemoveContext(request.Seq)
      ?? throw new Exception("Proxy request not found for evaluate");

    try
    {
      var setExprReq = new Request
      {
        Seq = 0,
        Type = "request",
        Command = "setExpression",
        Arguments = JsonSerializer.SerializeToElement(new
        {
          expression = lhs,
          value = rhs,
          frameId
        }, ProxyRequestOptions)
      };

      logger.LogDebug("[CLIENT] Rewriting evaluate assignment: lhs={lhs} rhs={rhs}", lhs, rhs);
      var setExprResp = await proxy.RunInternalRequestAsync(setExprReq, cancellationToken);

      Response evaluateResponse;
      if (!setExprResp.Success)
      {
        evaluateResponse = new Response
        {
          Seq = 0,
          Type = "response",
          RequestSeq = context.OriginalSeq,
          Success = false,
          Command = "evaluate",
          Message = setExprResp.Message,
          Body = null
        };
      }
      else
      {
        JsonElement? body = null;
        if (setExprResp.Body is { ValueKind: JsonValueKind.Object } setBody)
        {
          var remapped = new Dictionary<string, JsonElement>();
          foreach (var prop in setBody.EnumerateObject())
          {
            var key = prop.Name == "value" ? "result" : prop.Name;
            remapped[key] = prop.Value;
          }
          body = JsonSerializer.SerializeToElement(remapped, ProxyRequestOptions);
        }

        evaluateResponse = new Response
        {
          Seq = 0,
          Type = "response",
          RequestSeq = context.OriginalSeq,
          Success = true,
          Command = "evaluate",
          Body = body
        };
      }

      await proxy.WriteProxyToClientAsync(evaluateResponse, cancellationToken);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[CLIENT] evaluate->setExpression rewrite failed");
      await proxy.WriteProxyToClientAsync(new Response
      {
        Seq = 0,
        Type = "response",
        RequestSeq = context.OriginalSeq,
        Success = false,
        Command = "evaluate",
        Message = ex.Message,
        Body = null
      }, cancellationToken);
    }

    return null;
  }

  private static (string Lhs, string Rhs)? TryParseAssignment(string expression)
  {
    var depth = 0;
    var i = 0;
    while (i < expression.Length)
    {
      var c = expression[i];

      if (c == '"' || c == '\'')
      {
        var quote = c;
        i++;
        while (i < expression.Length && expression[i] != quote)
        {
          if (expression[i] == '\\' && i + 1 < expression.Length)
          {
            i += 2;
            continue;
          }
          i++;
        }
        if (i < expression.Length)
        {
          i++;
        }
        continue;
      }

      if (c == '(' || c == '[' || c == '{')
      {
        depth++;
      }
      else if (c == ')' || c == ']' || c == '}')
      {
        if (depth > 0)
        {
          depth--;
        }
      }
      else if (c == '=' && depth == 0)
      {
        var prev = i > 0 ? expression[i - 1] : '\0';
        var next = i + 1 < expression.Length ? expression[i + 1] : '\0';
        if (next == '=')
        {
          i += 2;
          continue;
        }
        if (next == '>' || prev == '!' || prev == '<' || prev == '>')
        {
          i++;
          continue;
        }

        var lhs = expression[..i].Trim();
        var rhs = expression[(i + 1)..].Trim();
        if (lhs.Length == 0 || rhs.Length == 0)
        {
          return null;
        }
        if (!IsValidAssignmentTarget(lhs))
        {
          return null;
        }
        return (lhs, rhs);
      }

      i++;
    }
    return null;
  }

  private static bool IsValidAssignmentTarget(string lhs)
  {
    if (lhs.Length == 0 || lhs.Contains(".."))
    {
      return false;
    }

    var first = lhs[0];
    if (!(first == '_' || char.IsLetter(first)))
    {
      return false;
    }

    var bracketDepth = 0;
    foreach (var c in lhs)
    {
      if (c == '[')
      {
        bracketDepth++;
      }
      else if (c == ']')
      {
        if (bracketDepth == 0)
        {
          return false;
        }
        bracketDepth--;
      }
      else if (bracketDepth > 0)
      {
        // any char allowed inside indexer
      }
      else if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
      {
        return false;
      }
    }

    return bracketDepth == 0;
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

  private ScopesRequest HandleScopesRequest(ScopesRequest request, IDebuggerProxy proxy)
  {
    if (frameSourceTracker is not null && request.Arguments is not null)
    {
      try
      {
        var originalSeq = proxy.PeekOriginalSeq(request.Seq);
        if (originalSeq is not null)
        {
          frameSourceTracker.RecordScopesRequest(originalSeq.Value, request.Arguments.FrameId);
        }
      }
      catch (Exception ex)
      {
        logger.LogDebug(ex, "HandleScopesRequest tracking failed");
      }
    }

    logger.LogDebug("[CLIENT] Scopes request: frameId={frameId}", request.Arguments?.FrameId);
    return request;
  }

  private void TrackVariablesRequest(InterceptableVariablesRequest request, IDebuggerProxy proxy)
  {
    if (frameSourceTracker is null || request.Arguments is null)
    {
      return;
    }

    try
    {
      var originalSeq = proxy.PeekOriginalSeq(request.Seq);
      if (originalSeq is not null)
      {
        frameSourceTracker.RecordVariablesRequest(originalSeq.Value, request.Arguments.VariablesReference);
      }
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "TrackVariablesRequest failed");
    }
  }

  private Request LogAndPassthrough(Request request)
  {
    if (request.Command == "configurationDone")
    {
      onConfigurationDone();
    }
    logger.LogDebug("[CLIENT] Request: {command}", request.Command);
    return request;
  }
}