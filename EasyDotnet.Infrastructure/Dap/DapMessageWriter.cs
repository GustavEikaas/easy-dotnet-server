using System.Text;

namespace EasyDotnet.Infrastructure.Dap;

public class DapMessageWriter
{

  public static async Task WriteDapMessageAsync(string json, Stream stream, CancellationToken cancellationToken)
  {
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

    await stream.WriteAsync(header, cancellationToken);
    await stream.WriteAsync(bytes, cancellationToken);
    await stream.FlushAsync(cancellationToken);
  }
}