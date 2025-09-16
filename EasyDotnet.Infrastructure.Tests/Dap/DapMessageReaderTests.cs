using System.Text;
using System.Text.Json;
using EasyDotnet.Infrastructure.Dap;
using Nerdbank.Streams;

namespace EasyDotnet.Infrastructure.Tests.Dap;

public class DapMessageReaderTests
{
  private static (Stream client, Stream server) CreateStreamPair()
         => FullDuplexStream.CreatePair();

  private static async Task WriteDapMessageAsync(Stream stream, string json)
  {
    var bytes = Encoding.UTF8.GetBytes(json);
    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    await stream.WriteAsync(header);
    await stream.WriteAsync(bytes);
    await stream.FlushAsync();
  }

  [Test]
  public async Task DapMessageIsReadCorrectly()
  {
    var (client, server) = CreateStreamPair();

    var message = JsonSerializer.Serialize(new
    {
      seq = 1,
      type = "request",
      command = "initialize"
    });
    await WriteDapMessageAsync(client, message);

    var result = await DapMessageReader.ReadDapMessageAsync(server, default);

    await Assert.That(result).IsEqualTo(message);
  }

  [Test]
  public async Task MultipleMessagesAreReadCorrectly()
  {
    var (client, server) = CreateStreamPair();

    var msg1 = JsonSerializer.Serialize(new
    {
      seq = 1,
      type = "request",
      command = "initialize"
    });

    var msg2 = JsonSerializer.Serialize(new
    {
      seq = 2,
      type = "request",
      command = "launch"
    });
    await WriteDapMessageAsync(client, msg1);
    await WriteDapMessageAsync(client, msg2);

    var result1 = await DapMessageReader.ReadDapMessageAsync(server, default);
    var result2 = await DapMessageReader.ReadDapMessageAsync(server, default);

    await Assert.That(result1).IsEqualTo(msg1);
    await Assert.That(result2).IsEqualTo(msg2);
  }

  [Test]
  public async Task MissingHeaderReturnsNull()
  {
    var (client, server) = CreateStreamPair();

    var badHeader = Encoding.UTF8.GetBytes("Invalid-Header: 999\r\n\r\n");
    await client.WriteAsync(badHeader);
    await client.FlushAsync();

    var result = await DapMessageReader.ReadDapMessageAsync(server, default);

    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task MalformedHeaderThrowsFormatException()
  {
    var (client, server) = CreateStreamPair();

    var badHeader = Encoding.UTF8.GetBytes("Content-Length: not-a-number\r\n\r\n");
    await client.WriteAsync(badHeader);
    await client.FlushAsync();

    await Assert.ThrowsAsync<FormatException>(async () => await DapMessageReader.ReadDapMessageAsync(server, default));
  }

  [Test]
  public async Task TruncatedBodyReturnsNull()
  {
    var (client, server) = CreateStreamPair();

    var msg = JsonSerializer.Serialize(new
    {
      seq = 1,
      type = "request",
      command = "initialize"
    });
    var bytes = Encoding.UTF8.GetBytes(msg);

    var header = Encoding.UTF8.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
    await client.WriteAsync(header);
    await client.WriteAsync(bytes.AsMemory(0, bytes.Length / 2));
    await client.FlushAsync();
    client.Dispose();

    var result = await DapMessageReader.ReadDapMessageAsync(server, default);

    await Assert.That(result).IsNull();
  }
}