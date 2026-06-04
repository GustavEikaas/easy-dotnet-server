using EasyDotnet.ProjXLanguageServer.Protocol;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EasyDotnet.ProjXLanguageServer.Tests.Protocol;

public class ProtocolSerializationTests
{
  [Test]
  public async Task InlineCompletionItem_UsesLspFieldNames()
  {
    var item = new InlineCompletionItem
    {
      InsertText = "></TargetFramework>",
      FilterText = "TargetFramework"
    };

    var json = JObject.Parse(JsonConvert.SerializeObject(item));

    await Assert.That(json["insertText"]!.Value<string>()).IsEqualTo("></TargetFramework>");
    await Assert.That(json["filterText"]!.Value<string>()).IsEqualTo("TargetFramework");
    await Assert.That(json["InsertText"]).IsNull();
  }

  [Test]
  public async Task InlineCompletionParams_UsesLspFieldNames()
  {
    var @params = new InlineCompletionParams
    {
      TextDocument = new TextDocumentIdentifier { Uri = new Uri("file:///tmp/test.csproj") },
      Position = new Position { Line = 1, Character = 2 },
      Context = new InlineCompletionContext { TriggerKind = InlineCompletionTriggerKind.Automatic },
      WorkDoneToken = "token"
    };

    var json = JObject.Parse(JsonConvert.SerializeObject(@params));

    await Assert.That(json["textDocument"]!["uri"]!.Value<string>()).IsEqualTo("file:///tmp/test.csproj");
    await Assert.That(json["position"]!["line"]!.Value<int>()).IsEqualTo(1);
    await Assert.That(json["context"]!["triggerKind"]!.Value<int>()).IsEqualTo(2);
    await Assert.That(json["workDoneToken"]!.Value<string>()).IsEqualTo("token");
  }

  [Test]
  public async Task ServerCapabilities_AdvertisesInlineCompletionProvider()
  {
    var capabilities = new ProjXServerCapabilities
    {
      InlineCompletionProvider = new InlineCompletionOptions()
    };

    var json = JObject.Parse(JsonConvert.SerializeObject(capabilities));

    await Assert.That(json["inlineCompletionProvider"]).IsNotNull();
  }
}
