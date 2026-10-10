using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using EasyDotnet.ProjXLanguageServer.Utils;

namespace EasyDotnet.ProjXLanguageServer.Tests.Utils;

public class PackageReferenceAttributeTests
{
  [Test]
  [Arguments("PackageReference")]
  [Arguments("PackageVersion")]
  public async Task InsideIncludeValue_ReturnsAttributeContextWithIncludePrefix(string attribute)
  {
    var text = $"<Project>\n<ItemGroup>\n<{attribute} Include=\"Newt@CURSOR\" Version=\"1.0.0\" />\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);

    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.InsideAttributeValue);
    await Assert.That(ctx.ElementName).IsEqualTo(attribute);
    await Assert.That(ctx.AttributeName).IsEqualTo("Include");
    await Assert.That(ctx.Attributes!["Include"]).IsEqualTo("Newt");
    await Assert.That(ctx.Attributes!["Version"]).IsEqualTo("1.0.0");
  }

  [Test]
  [Arguments("PackageReference")]
  [Arguments("PackageVersion")]
  public async Task InsideVersionValue_ExposesSiblingIncludeId(string attribute)
  {
    var text = $"<Project>\n<ItemGroup>\n<{attribute} Include=\"Newtonsoft.Json\" Version=\"13.@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);

    await Assert.That(ctx.Kind).IsEqualTo(CursorContextKind.InsideAttributeValue);
    await Assert.That(ctx.AttributeName).IsEqualTo("Version");
    await Assert.That(ctx.Attributes!["Include"]).IsEqualTo("Newtonsoft.Json");
  }
}