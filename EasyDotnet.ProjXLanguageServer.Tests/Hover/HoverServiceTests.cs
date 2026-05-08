using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Hover;

public class HoverServiceTests
{
  private static HoverService Build()
  {
    var fs = new MockFileSystem();
    var resolver = new UserSecretsResolver(fs);
    return new HoverService(resolver);
  }

  [Test]
  public async Task Hover_OnUserSecretsIdGuid_ReturnsSecretsPath()
  {
    var guid = "00000000-0000-0000-0000-000000000000";
    var text = $"<Project>\n<PropertyGroup>\n<UserSecretsId>@CURSOR{guid}</UserSecretsId>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var hover = Build().GetHover(Docs.Make(clean), line, character);
    await Assert.That(hover).IsNotNull();
    var content = (MarkupContent)hover!.Contents.Value!;
    await Assert.That(content.Value).Contains(guid);
    await Assert.That(content.Value).Contains("secrets.json");
  }

  [Test]
  public async Task Hover_OnPropertyName_ReturnsDocumentation()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>@CURSORnet8.0</TargetFramework>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var hover = Build().GetHover(Docs.Make(clean), line, character);
    await Assert.That(hover).IsNotNull();
    var content = (MarkupContent)hover!.Contents.Value!;
    await Assert.That(content.Value).Contains("TargetFramework");
  }

  [Test]
  public async Task Hover_OnUnrelatedText_ReturnsNull()
  {
    var clean = "<Project>\n<PropertyGroup>\n</PropertyGroup>\n</Project>";
    var hover = Build().GetHover(Docs.Make(clean), 0, 0);
    await Assert.That(hover).IsNull();
  }
}