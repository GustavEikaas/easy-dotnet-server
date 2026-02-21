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
  public string? Console { get; set; }
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

public enum RunInTerminalKind
{
  Internal = 0,
  External = 1
}

public class RunInTerminalRequest : Request
{
  public new RunInTerminalRequestArguments Arguments { get; set; } = new();

  public static RunInTerminalRequest Create(RunInTerminalKind kind, string[] args) => new()
  {
    Type = "request",
    Seq = 0,
    Command = "runInTerminal",
    Arguments = new RunInTerminalRequestArguments()
    {
      Kind = kind == RunInTerminalKind.External ? "external" : "integrated",
      Title = "EasyDotnet",
      Args = args,
      Cwd = ".",
      ArgsCanBeInterpretedByShell = false
    }
  };
}

public class RunInTerminalRequestArguments
{
  /**
   * What kind of terminal to launch. Defaults to `integrated` if not specified.
   * Values: 'integrated', 'external'
   */
  public string Kind { get; set; } = "integrated";

  /**
   * Title of the terminal.
   */
  public string? Title { get; set; }

  /**
   * Working directory for the command. For non-empty, valid paths this
   * typically results in execution of a change directory command.
   */
  public string? Cwd { get; set; }

  /**
   * List of arguments. The first argument is the command to run.
   */
  public string[] Args { get; set; } = [];

  /**
   * Environment key-value pairs that are added to or removed from the default
   * environment.
   */
  public Dictionary<string, string> Env = [];

  /**
   * This property should only be set if the corresponding capability
   * `supportsArgsCanBeInterpretedByShell` is true. If the client uses an
   * intermediary shell to launch the application, then the client must not
   * attempt to escape characters with special meanings for the shell. The user
   * is fully responsible for escaping as needed and that arguments using
   * special characters may not be portable across shells.
   */
  public bool ArgsCanBeInterpretedByShell { get; set; }
}

public class RunInTerminalResponse : Response
{
  public new RunInTerminalResponseBody Body { get; set; } = new();
}

public class RunInTerminalResponseBody
{
  /**
   * The process ID. The value should be less than or equal to 2147483647
   * (2^31-1).
   */
  public int ProcessId;

  /**
   * The process ID of the terminal shell. The value should be less than or
   * equal to 2147483647 (2^31-1).
   */
  public int ShellProcessId;
}