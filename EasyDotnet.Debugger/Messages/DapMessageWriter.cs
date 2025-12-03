using System.Text;
using System.Text.Json;

namespace EasyDotnet.Debugger.Messages;

public static class DapMessageWriter
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

  /// <summary>
  /// Writes a raw JSON DAP message to the specified stream, including the required 
  /// <c>Content-Length</c> header, followed by the message body.
  /// </summary>
  /// <param name="json">The raw JSON string to write.</param>
  /// <param name="stream">The target <see cref="Stream"/> to write the message to.</param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
  /// </param>
  public static async Task WriteDapMessageAsync(string json, Stream stream, CancellationToken cancellationToken)
  {
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

    await stream.WriteAsync(header, cancellationToken);
    await stream.WriteAsync(bytes, cancellationToken);
    await stream.FlushAsync(cancellationToken);
  }

  /// <summary>
  /// Serializes the specified object to JSON using camelCase naming policy, then writes it 
  /// as a DAP message to the given stream. The message is prefixed with the required 
  /// <c>Content-Length</c> header.
  /// </summary>
  /// <param name="message">The object to serialize and send as a DAP message.</param>
  /// <param name="stream">The target <see cref="Stream"/> to write the message to.</param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
  /// </param>
  public static async Task WriteDapMessageAsync(object message, Stream stream, CancellationToken cancellationToken)
  {
    var json = JsonSerializer.Serialize(message, SerializerOptions);
    await WriteDapMessageAsync(json, stream, cancellationToken);
  }
}