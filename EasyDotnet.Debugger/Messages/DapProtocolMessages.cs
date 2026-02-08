using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Debugger.Messages;

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

public class ErrorResponse : ProtocolMessage
{
  [JsonPropertyName("request_seq")]
  public int RequestSeq { get; set; }
  public bool Success { get; set; }
  public string? Message { get; set; }
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
  public string[]? Args { get; set; }
  public Dictionary<string, string>? Env { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement> Other { get; set; } = [];
}

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

public class VariablesResponse : Response
{
  public new VariablesResponseBody? Body { get; set; }
}

public class VariablesResponseBody
{
  public required List<Variable> Variables { get; set; }
}

public class Variable
{
  public required string Name { get; set; }
  public required string Value { get; set; }
  public required string Type { get; set; }
  public string? EvaluateName { get; set; }
  public int? VariablesReference { get; set; }
  public int? NamedVariables { get; set; }

  [JsonExtensionData]
  public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}


public class InterceptableVariablesRequest : Request
{
  public new InterceptableVariablesArguments? Arguments { get; set; } = new();
}

public class InterceptableVariablesArguments
{
  public int VariablesReference { get; set; }
  [JsonExtensionData]
  public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public class TelemetryEvent : Event;

public class Metrics
{
  public required double CpuPercent { get; init; }
  public required double MemoryBytes { get; init; }
  public required long Timestamp { get; init; }
}