using System.Text.Json;
using System.Text.Json.Serialization;
using EasyDotnet.Infrastructure.Services;

namespace EasyDotnet.Infrastructure.Dap;

public class ProtocolMessage
{
  public required int Seq { get; set; }
  public required string Type { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement> ExtraProperties { get; set; } = [];
}

public class Request : ProtocolMessage
{
  public required string Command { get; set; }
  public JsonElement? Arguments { get; set; }
}

public class Event : ProtocolMessage
{
  [JsonPropertyName("event")]
  public required string EventName { get; set; }
  public JsonElement? Body { get; set; }
}

public class Response : ProtocolMessage
{
  [JsonPropertyName("request_seq")]
  public required int RequestSeq { get; set; }
  public required bool Success { get; set; }
  public required string Command { get; set; }
  public string? Message { get; set; }
  public JsonElement? Body { get; set; }
}

public class ErrorResponse : Response
{
  public new required ErrorBody Body { get; set; }
}

public class ErrorBody
{
  public JsonElement? Error { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? ExtraProperties { get; set; } = [];
}

public class InterceptableAttachRequest : Request
{
  public new InterceptableAttachArguments Arguments { get; set; } = new();
}

public class InterceptableAttachArguments
{
  public string? Request { get; set; }
  public string? Program { get; set; }
  public int? ProcessId { get; set; }
  public string? Cwd { get; set; }
  public Dictionary<string, string>? Env { get; set; }
}

public class InterceptableVariablesResponse : Response
{
  public new InterceptableVariablesResponseBody Body { get; set; } = new();
}

public class InterceptableVariablesResponseBody
{
  public List<InterceptableVariable> Variables { get; set; } = new();
}

public class InterceptableVariable
{
  public string? EvaluateName { get; set; }

  public string Name { get; set; } = string.Empty;

  public string? Type { get; set; }

  public string? Value { get; set; }

  public int VariablesReference { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? NamedVariables { get; set; }
}

public record InternalVariablesRequest
{
  public int Seq { get; init; }

  public string Type { get; init; } = "request";

  public string Command { get; init; } = "variables";

  public InternalVariablesArguments Arguments { get; init; } = new();
}

public record InternalVariablesArguments
{
  public int VariablesReference { get; init; }
}

public class VariablesRequest : Request
{
  public new VariablesRequestArguments Arguments { get; set; } = new();

  [JsonIgnore]
  public bool IsInternalVarRequest => Arguments.VariablesReference >= NetcoreDbgService.InternalVarRefBase;
}

public class VariablesRequestArguments
{
  [JsonPropertyName("variablesReference")]
  public int VariablesReference { get; set; }
public class SetBreakpointsRequest : Request
{
  public new required SetBreakpointsArguments Arguments { get; set; }
}

public class SetBreakpointsArguments
{
  public required List<Breakpoint> Breakpoints { get; set; }
  public required List<int> Lines { get; set; }
  public required Source Source { get; set; }
  public required bool SourceModified { get; set; }
}

public class Breakpoint
{
  public required int Line { get; set; }
}

public class Source
{
  public required string Name { get; set; }
  public required string Path { get; set; }
}