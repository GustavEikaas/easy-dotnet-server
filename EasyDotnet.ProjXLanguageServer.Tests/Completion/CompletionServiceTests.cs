using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;

namespace EasyDotnet.ProjXLanguageServer.Tests.Completion;

public class CompletionServiceTests
{
  private static readonly CompletionService Sut = CompletionTestFactory.Create();

  [Test]
  public async Task PropertyGroup_OffersTargetFramework()
  {
    var text = "<Project>\n<PropertyGroup>\n@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task ItemGroup_OffersPackageReference()
  {
    var text = "<Project>\n<ItemGroup>\n@CURSOR\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "PackageReference")).IsTrue();
  }

  [Test]
  public async Task StaticCompletion_DoesNotReportProgress()
  {
    var progress = new FakeLspProgressReporter();
    var sut = CompletionTestFactory.Create(progress: progress);

    var text = "<Project>\n<ItemGroup>\n@CURSOR\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(progress.Calls).IsEqualTo(0);
  }

  [Test]
  public async Task TargetBody_OffersMessageTask()
  {
    var text = "<Project>\n<Target Name=\"Build\">\n@CURSOR\n</Target>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    var message = items.SingleOrDefault(i => i.Label == "Message");

    await Assert.That(message).IsNotNull();
    await Assert.That(message!.Kind).IsEqualTo(Microsoft.VisualStudio.LanguageServer.Protocol.CompletionItemKind.Class);
    await Assert.That(message!.InsertText).IsEqualTo("Message Text=\"$1\" Importance=\"$2\" />");
  }

  [Test]
  public async Task PartialTagInTargetBody_OffersMessageTask()
  {
    var text = "<Project>\n<Target Name=\"Build\">\n<M@CURSOR\n</Target>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "Message")).IsTrue();
  }

  [Test]
  public async Task PropertyGroup_DoesNotOfferMessageTask()
  {
    var text = "<Project>\n<PropertyGroup>\n@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "Message")).IsFalse();
  }

  [Test]
  public async Task ProjectSdkAttribute_OffersKnownSdks()
  {
    var text = "<Project Sdk=\"@CURSOR\">\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    var labels = items.Select(i => i.Label).ToArray();

    await Assert.That(labels).Contains("Microsoft.NET.Sdk");
    await Assert.That(labels).Contains("Microsoft.NET.Sdk.Web");
    await Assert.That(labels).Contains("Microsoft.NET.Sdk.Razor");
    await Assert.That(labels).Contains("Microsoft.NET.Sdk.Worker");
    await Assert.That(labels).Contains("Microsoft.NET.Sdk.BlazorWebAssembly");
  }

  [Test]
  public async Task ProjectNonSdkAttribute_DoesNotOfferKnownSdks()
  {
    var text = "<Project ToolsVersion=\"@CURSOR\">\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "Microsoft.NET.Sdk")).IsFalse();
  }

  [Test]
  public async Task ProjectRoot_OffersPropertyGroup()
  {
    var text = "<Project>\n@CURSOR\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "PropertyGroup")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "ItemGroup")).IsTrue();
  }

  [Test]
  public async Task TargetFrameworkValue_OffersNet8()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>@CURSOR</TargetFramework>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "net8.0")).IsTrue();
  }

  [Test]
  public async Task NullableValue_OffersEnable()
  {
    var text = "<Project>\n<PropertyGroup>\n<Nullable>@CURSOR</Nullable>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "enable")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "disable")).IsTrue();
  }

  [Test]
  public async Task UserSecretsId_OffersGuid()
  {
    var text = "<Project>\n<PropertyGroup>\n<UserSecretsId>@CURSOR</UserSecretsId>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Length).IsEqualTo(1);
    await Assert.That(Guid.TryParse(items[0].InsertText, out _)).IsTrue();
  }

  [Test]
  public async Task PartialFullWordTagInPropertyGroup_OffersPropertyCompletions()
  {
    var text = "<Project>\n<PropertyGroup>\n<Target@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "TargetFrameworks")).IsTrue();
  }

  [Test]
  public async Task PartialTagInPropertyGroup_OffersPropertyCompletions()
  {
    var text = "<Project>\n<PropertyGroup>\n<Tar@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = (await Sut.GetCompletionsAsync(Docs.Make(clean), line, character, default)).Items;
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "TargetFrameworks")).IsTrue();
  }

  [Test]
  public async Task UnknownContext_ReturnsEmpty()
  {
    var text = "@CURSOR";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var items = (await Sut.GetCompletionsAsync(Docs.Make(string.Empty), line, character, default)).Items;
    await Assert.That(items.Length).IsEqualTo(0);
  }
}