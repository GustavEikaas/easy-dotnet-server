using System.Text;
using System.Text.Json;

namespace EasyDotnet.Infrastructure.Dap;

public class DapMessageWriter
{
  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

  public static async Task WriteDapMessageAsync(string json, Stream stream, CancellationToken cancellationToken)
  {
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");

    await stream.WriteAsync(header, cancellationToken);
    await stream.WriteAsync(bytes, cancellationToken);
    await stream.FlushAsync(cancellationToken);
  }

  public static async Task WriteDapMessageAsync(object message, Stream stream, CancellationToken cancellationToken)
  {
    var json = JsonSerializer.Serialize(message, SerializerOptions);
    await WriteDapMessageAsync(json, stream, cancellationToken);
  }
}