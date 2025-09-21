using System.Text;
using System.Text.Json;
using EasyDotnet.Infrastructure.Dap;
using Nerdbank.Streams;

namespace EasyDotnet.Infrastructure.Tests.Dap;

public class DapMessageReaderTests
{
  private static (Stream client, Stream server) CreateStreamPair()
         => FullDuplexStream.CreatePair();

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
  };

  private record InitializeRequest(int Seq, string Type, string Command);

  private static async Task WriteDapMessageAsync(Stream stream, string json) => await DapMessageWriter.WriteDapMessageAsync(json, stream, default);

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
  public async Task DapMessageObjectOverloadSerializesCorrectly()
  {
    var (client, server) = CreateStreamPair();

    var obj = new
    {
      Seq = 1,
      Type = "request",
      Command = "initialize"
    };

    await DapMessageWriter.WriteDapMessageAsync(obj, client, default);

    var result = await DapMessageReader.ReadDapMessageAsync(server, default);

    var expected = JsonSerializer.Serialize(obj, SerializerOptions);

    await Assert.That(result).IsEqualTo(expected);
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

  [Test]
  public async Task DeserializedMessageIsReadCorrectly()
  {
    var (client, server) = CreateStreamPair();

    var obj = new InitializeRequest(1, "request", "initialize");

    await DapMessageWriter.WriteDapMessageAsync(obj, client, default);

    var result = await DapMessageReader.ReadDapMessageDeserializedAsync<InitializeRequest>(server, default);

    await Assert.That(result).IsNotNull();
    await Assert.That(result!.Seq).IsEqualTo(1);
    await Assert.That(result.Type).IsEqualTo("request");
    await Assert.That(result.Command).IsEqualTo("initialize");
  }


  [Test]
  public async Task DeserializedMessageWithUnexpectedShapeYieldsDefaultValues()
  {
    var (client, server) = CreateStreamPair();

    var msg = JsonSerializer.Serialize(new { foo = "bar" }, SerializerOptions);
    await WriteDapMessageAsync(client, msg);

    var result = await DapMessageReader.ReadDapMessageDeserializedAsync<InitializeRequest>(server, default);

    await Assert.That(result).IsNotNull();
    await Assert.That(result!.Seq).IsEqualTo(0);
    await Assert.That(result.Type).IsNull();
    await Assert.That(result.Command).IsNull();
  }

  [Test]
  public async Task DeserializedMessageThrowsOnInvalidJson()
  {
    var (client, server) = CreateStreamPair();

    var badJson = "{ not-valid-json }";
    await DapMessageWriter.WriteDapMessageAsync(badJson, client, default);

    await Assert.ThrowsAsync<JsonException>(
      async () => await DapMessageReader.ReadDapMessageDeserializedAsync<InitializeRequest>(server, default));
  }

  [Test]
  public void DeserializedMessageRespectsCancellation()
  {
    var (client, server) = CreateStreamPair();
    var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.ThrowsAsync<OperationCanceledException>(
      async () => await DapMessageReader.ReadDapMessageDeserializedAsync<InitializeRequest>(server, cts.Token));
  }
}