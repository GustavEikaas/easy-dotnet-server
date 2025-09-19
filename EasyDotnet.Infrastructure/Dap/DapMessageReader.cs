using System.Text;

namespace EasyDotnet.Infrastructure.Dap;

public class DapMessageReader
{
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