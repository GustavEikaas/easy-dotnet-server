using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyDotnet.Infrastructure.Dap;

/// <summary>
/// Represents a DAP error response message with the proper structure.
/// </summary>
public sealed record DapErrorMessage(
    string Command,       // The DAP command that caused the error
    int Seq,              // The sequence number for this response
    int RequestSeq,       // The sequence number of the original request
    string Message        // The human-readable error message
)
{
  /// <summary>
  /// Returns an object representing the DAP error response.
  /// </summary>
  public DapErrorResponse ToResponseObject() =>
      new(
          Type: "response",
          Seq: Seq,
          RequestSeq: RequestSeq,
          Success: false,
          Command: Command,
          Message: Message,
          Body: new DapErrorBody(new DapErrorDetails(1, Message))
      );

  /// <summary>
  /// Returns the serialized JSON string of the DAP error response.
  /// </summary>
  public string ToSerializedResponse(JsonSerializerOptions? options = null) =>
      JsonSerializer.Serialize(ToResponseObject(), options ?? new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
      });
}

/// <summary>
/// Represents the full DAP error response object.
/// </summary>
public sealed record DapErrorResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("request_seq")] int RequestSeq,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("body")] DapErrorBody Body
);
/// <summary>
/// Represents the body containing the error details.
/// </summary>
public sealed record DapErrorBody([property: JsonPropertyName("error")] DapErrorDetails Error);

/// <summary>
/// Represents the actual error details.
/// </summary>
public sealed record DapErrorDetails(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("format")] string Format
);