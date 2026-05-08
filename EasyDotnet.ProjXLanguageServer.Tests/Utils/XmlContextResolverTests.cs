using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using EasyDotnet.ProjXLanguageServer.Utils;

namespace EasyDotnet.ProjXLanguageServer.Tests.Utils;

public class XmlContextResolverTests
{
  [Test]
  public async Task InsidePropertyGroup_BetweenElements_ReturnsPropertyGroup()
  {
    var text = "<Project Sdk=\"Microsoft.NET.Sdk\">\n<PropertyGroup>\n@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var doc = Docs.Make(clean);
    var ctx = XmlContextResolver.Resolve(doc, line, character);
    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.PropertyGroup);
  }

  [Test]
  public async Task InsideItemGroup_BetweenElements_ReturnsItemGroup()
  {
    var text = "<Project>\n<ItemGroup>\n@CURSOR\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);
    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.ItemGroup);
  }

  [Test]
  public async Task InsideElementText_TargetFramework_ReturnsTextContext()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>@CURSOR</TargetFramework>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);
    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.InsideElementText);
    await Assert.That(ctx.ElementName).IsEqualTo("TargetFramework");
  }

  [Test]
  public async Task InsideAttributeValue_PackageReferenceInclude_ReturnsAttribute()
  {
    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"@CURSOR\" Version=\"1.0.0\" />\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);
    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.InsideAttributeValue);
    await Assert.That(ctx.AttributeName).IsEqualTo("Include");
    await Assert.That(ctx.ElementName).IsEqualTo("PackageReference");
  }
}