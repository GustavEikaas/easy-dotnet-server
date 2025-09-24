using System.Text.Json;
using EasyDotnet.Infrastructure.Dap;
using Namotion.Reflection;

namespace EasyDotnet.Infrastructure.Tests.Dap;

public class DapMessageDeserializerTests
{

  [Test]
  public async Task Parse_ValidRequestJson_ReturnsRequest()
  {
    var json = @"{
                ""seq"": 1,
                ""type"": ""request"",
                ""command"": ""initialize"",
                ""arguments"": { ""someArg"": 123 }
            }";

    var result = DapMessageDeserializer.Parse(json);

    await Assert.That(result).IsTypeOf<DAP.Request>();
    var req = (DAP.Request)result;
    await Assert.That(req.Seq).IsEqualTo(1);
    await Assert.That(req.Command).IsEqualTo("initialize");
  }

  [Test]
  public async Task Parse_ValidEventJson_ReturnsEvent()
  {
    var json = @"{
                ""seq"": 2,
                ""type"": ""event"",
                ""event"": ""stopped"",
                ""body"": { ""reason"": ""breakpoint"" }
            }";

    var result = DapMessageDeserializer.Parse(json);

    await Assert.That(result).IsTypeOf<DAP.Event>();
    var evt = (DAP.Event)result;

    await Assert.That(evt.Seq).IsEqualTo(2);
    await Assert.That(evt.EventName).IsEqualTo("stopped");
  }

  [Test]
  public async Task Parse_ValidResponseJson_ReturnsResponse()
  {
    var json = @"{
                ""seq"": 3,
                ""type"": ""response"",
                ""request_seq"": 1,
                ""success"": true,
                ""command"": ""initialize"",
                ""body"": { ""capabilities"": {} }
            }";

    var result = DapMessageDeserializer.Parse(json);

    await Assert.That(result).IsTypeOf<DAP.Response>();
    var res = (DAP.Response)result;

    await Assert.That(res.Seq).IsEqualTo(3);
    await Assert.That(res.RequestSeq).IsEqualTo(1);
    await Assert.That(res.Success).IsTrue();
    await Assert.That(res.Command).IsEqualTo("initialize");
  }

  [Test]
  public async Task Parse_InvalidJson_ThrowsException()
  {
    var json = @"{ ""invalid"": true }";

    await Assert.ThrowsAsync<JsonException>(() => Task.Run(() => DapMessageDeserializer.Parse(json)));
  }

  [Test]
  public async Task Parse_EmptyJson_ThrowsArgumentNullException()
  {
    var json = "";

    await Assert.ThrowsAsync<ArgumentNullException>(() => Task.Run(() => DapMessageDeserializer.Parse(json)));
  }
}