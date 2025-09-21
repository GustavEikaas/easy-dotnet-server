using System.Text;
using System.Text.Json;

namespace EasyDotnet.Infrastructure.Dap;

public static class DapMessageReader
{
  private static readonly JsonSerializerOptions DeserializerOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  /// <summary>
  /// Reads a single DAP message from the specified stream asynchronously and returns the raw JSON string.
  /// </summary>
  /// <param name="stream">
  /// The <see cref="Stream"/> to read the message from. The stream must contain a valid 
  /// DAP message prefixed with a <c>Content-Length</c> header followed by <c>\r\n\r\n</c>.
  /// </param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
  /// </param>
  /// <returns>
  /// A <see cref="Task{TResult}"/> representing the asynchronous read operation.
  /// The result contains the JSON string body of the DAP message if successfully read;
  /// otherwise, <c>null</c> if the stream ended before a complete message could be read.
  /// </returns>
  /// <exception cref="OperationCanceledException">
  /// Thrown if the operation was canceled via the <paramref name="cancellationToken"/>.
  /// </exception>
  /// <exception cref="IOException">
  /// Thrown if an I/O error occurs while reading from the stream.
  /// </exception>
  /// <exception cref="ObjectDisposedException">
  /// Thrown if the <paramref name="stream"/> has been disposed.
  /// </exception>
  public static async Task<string?> ReadDapMessageAsync(Stream stream, CancellationToken cancellationToken)
  {
    var headerBuilder = new StringBuilder();
    var buffer = new byte[1];

    while (true)
    {
      var n = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
      if (n == 0) return null;
      var c = (char)buffer[0];
      headerBuilder.Append(c);

      if (headerBuilder.Length >= 4 &&
          headerBuilder[^4] == '\r' &&
          headerBuilder[^3] == '\n' &&
          headerBuilder[^2] == '\r' &&
          headerBuilder[^1] == '\n')
        break;
    }

    var headers = headerBuilder.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    var contentLengthLine = headers.FirstOrDefault(h => h.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase));
    if (contentLengthLine == null) return null;
    var contentLength = int.Parse(contentLengthLine.Split(':')[1].Trim());

    var messageBytes = new byte[contentLength];
    var read = 0;
    while (read < contentLength)
    {
      var n = await stream.ReadAsync(messageBytes.AsMemory(read, contentLength - read), cancellationToken);
      if (n == 0) return null;
      read += n;
    }

    return Encoding.UTF8.GetString(messageBytes);
  }
  /// <summary>
  /// Reads a single DAP message from the specified stream asynchronously and 
  /// deserializes it into the given type <typeparamref name="T"/>.
  /// </summary>
  /// <typeparam name="T">The target type to deserialize the JSON message into.</typeparam>
  /// <param name="stream">The <see cref="Stream"/> to read the message from.</param>
  /// <param name="cancellationToken">
  /// A <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.
  /// </param>
  /// <returns>
  /// A <typeparamref name="T"/> instance if the message was successfully read and deserialized; 
  /// otherwise <c>default</c> if the stream ended before a complete message could be read.
  /// </returns>
  /// <exception cref="OperationCanceledException">
  /// Thrown if the operation was canceled via the <paramref name="cancellationToken"/>.
  /// </exception>
  /// <exception cref="IOException">
  /// Thrown if an I/O error occurs while reading from the stream.
  /// </exception>
  /// <exception cref="ObjectDisposedException">
  /// Thrown if the <paramref name="stream"/> has been disposed.
  /// </exception>
  /// <exception cref="JsonException">
  /// Thrown if the JSON is invalid, or cannot be deserialized into the target type <typeparamref name="T"/>.
  /// </exception>
  /// <exception cref="InvalidOperationException">
  /// Thrown if the deserializer encounters an unexpected condition while materializing the object.
  /// </exception>
  public static async Task<T?> ReadDapMessageDeserializedAsync<T>(Stream stream, CancellationToken cancellationToken)
  {
    var json = await ReadDapMessageAsync(stream, cancellationToken);
    return json == null ? default : JsonSerializer.Deserialize<T>(json, DeserializerOptions);
  }
}