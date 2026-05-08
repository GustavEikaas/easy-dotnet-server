using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;

namespace EasyDotnet.ProjXLanguageServer.Tests.Definition;

public class DefinitionServiceTests
{
  [Test]
  public async Task Definition_OnUserSecretsIdGuid_ReturnsSecretsLocationAndCreatesFile()
  {
    var fs = new MockFileSystem();
    var sut = new DefinitionService(new UserSecretsResolver(fs), fs);

    var guid = "44444444-4444-4444-4444-444444444444";
    var text = $"<Project>\n<PropertyGroup>\n<UserSecretsId>@CURSOR{guid}</UserSecretsId>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var loc = sut.GetDefinition(Docs.Make(clean), line, character);
    await Assert.That(loc).IsNotNull();
    await Assert.That(loc!.Uri.LocalPath).Contains(guid);
    await Assert.That(loc.Uri.LocalPath).EndsWith("secrets.json");
    await Assert.That(fs.File.Exists(loc.Uri.LocalPath)).IsTrue();
  }

  [Test]
  public async Task Definition_OnProjectReferenceInclude_ResolvesRelativeFile()
  {
    var fs = new MockFileSystem();
    fs.AddFile("/repo/Other/Other.csproj", new MockFileData("<Project/>"));
    var sut = new DefinitionService(new UserSecretsResolver(fs), fs);

    var text = "<Project>\n<ItemGroup>\n<ProjectReference Include=\"@CURSOR../Other/Other.csproj\" />\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var doc = Docs.Make(clean, "/repo/Self/Self.csproj");

    var loc = sut.GetDefinition(doc, line, character);
    await Assert.That(loc).IsNotNull();
    await Assert.That(loc!.Uri.LocalPath).EndsWith("Other.csproj");
  }

  [Test]
  public async Task Definition_OnImportProject_ResolvesRelativeFile()
  {
    var fs = new MockFileSystem();
    fs.AddFile("/repo/Build/common.props", new MockFileData("<Project/>"));
    var sut = new DefinitionService(new UserSecretsResolver(fs), fs);

    var text = "<Project>\n<Import Project=\"@CURSOR../Build/common.props\" />\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var doc = Docs.Make(clean, "/repo/Self/Self.csproj");

    var loc = sut.GetDefinition(doc, line, character);
    await Assert.That(loc).IsNotNull();
    await Assert.That(loc!.Uri.LocalPath).EndsWith("common.props");
  }

  [Test]
  public async Task Definition_OnMissingProjectReference_ReturnsNull()
  {
    var fs = new MockFileSystem();
    var sut = new DefinitionService(new UserSecretsResolver(fs), fs);

    var text = "<Project>\n<ItemGroup>\n<ProjectReference Include=\"@CURSOR../Nope.csproj\" />\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var doc = Docs.Make(clean, "/repo/Self/Self.csproj");

    var loc = sut.GetDefinition(doc, line, character);
    await Assert.That(loc).IsNull();
  }
}