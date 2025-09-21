using System.Text;

namespace EasyDotnet.Infrastructure.Dap;

public static class DapMessageReader
{
  /// <summary>
  /// Reads a single DAP message from the specified stream asynchronously.
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
  /// <remarks>
  /// This method reads the <c>Content-Length</c> header to determine how many bytes
  /// to read for the message body. The header and body are expected to follow the
  /// Debug Adapter Protocol message framing convention.
  /// </remarks>
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
}