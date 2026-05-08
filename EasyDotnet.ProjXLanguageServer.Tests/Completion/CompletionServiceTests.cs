using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;

namespace EasyDotnet.ProjXLanguageServer.Tests.Completion;

public class CompletionServiceTests
{
  private static readonly CompletionService Sut = new();

  [Test]
  public async Task PropertyGroup_OffersTargetFramework()
  {
    var text = "<Project>\n<PropertyGroup>\n@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task ItemGroup_OffersPackageReference()
  {
    var text = "<Project>\n<ItemGroup>\n@CURSOR\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "PackageReference")).IsTrue();
  }

  [Test]
  public async Task ProjectRoot_OffersPropertyGroup()
  {
    var text = "<Project>\n@CURSOR\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "PropertyGroup")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "ItemGroup")).IsTrue();
  }

  [Test]
  public async Task TargetFrameworkValue_OffersNet8()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>@CURSOR</TargetFramework>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "net8.0")).IsTrue();
  }

  [Test]
  public async Task NullableValue_OffersEnable()
  {
    var text = "<Project>\n<PropertyGroup>\n<Nullable>@CURSOR</Nullable>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "enable")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "disable")).IsTrue();
  }

  [Test]
  public async Task UserSecretsId_OffersGuid()
  {
    var text = "<Project>\n<PropertyGroup>\n<UserSecretsId>@CURSOR</UserSecretsId>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Length).IsEqualTo(1);
    await Assert.That(Guid.TryParse(items[0].InsertText, out _)).IsTrue();
  }

  [Test]
  public async Task PartialFullWordTagInPropertyGroup_OffersPropertyCompletions()
  {
    var text = "<Project>\n<PropertyGroup>\n<Target@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "TargetFrameworks")).IsTrue();
  }

  [Test]
  public async Task PartialTagInPropertyGroup_OffersPropertyCompletions()
  {
    var text = "<Project>\n<PropertyGroup>\n<Tar@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var items = Sut.GetCompletions(Docs.Make(clean), line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
    await Assert.That(items.Any(i => i.Label == "TargetFrameworks")).IsTrue();
  }

  [Test]
  public async Task UnknownContext_ReturnsEmpty()
  {
    var text = "@CURSOR";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var items = Sut.GetCompletions(Docs.Make(string.Empty), line, character);
    await Assert.That(items.Length).IsEqualTo(0);
  }
}