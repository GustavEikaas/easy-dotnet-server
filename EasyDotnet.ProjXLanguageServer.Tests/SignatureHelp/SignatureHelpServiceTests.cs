using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.SignatureHelp;

public class SignatureHelpServiceTests
{
  private static readonly SignatureHelpService Sut = new();

  private static (CsprojDocument doc, int line, int character) Pose(string text)
  {
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    return (Docs.Make(text.Replace("@CURSOR", string.Empty)), line, character);
  }

  [Test]
  public async Task PackageReferenceIncludeAttribute_ReturnsPackageIdSignature()
  {
    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"@CURSOR\" Version=\"1.0.0\" />\n</ItemGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("package id");
  }

  [Test]
  public async Task PackageReferenceVersionAttribute_ReturnsVersionSignature()
  {
    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"X\" Version=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("version");
  }

  [Test]
  public async Task ProjectReferenceInclude_ReturnsRelativePathSignature()
  {
    var text = "<Project>\n<ItemGroup>\n<ProjectReference Include=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("relative path");
  }

  [Test]
  public async Task ConditionAttribute_AnywhereReturnsConditionSignature()
  {
    var text = "<Project>\n<PropertyGroup Condition=\"@CURSOR\">\n</PropertyGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("expression");
  }

  [Test]
  public async Task PackageReferencePrivateAssets_ReturnsAssetsSignature()
  {
    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"X\" Version=\"1\" PrivateAssets=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("PrivateAssets");
  }

  [Test]
  public async Task ProjectSdkAttribute_ReturnsSdkSignature()
  {
    var text = "<Project Sdk=\"@CURSOR\">\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("sdk id");
  }

  [Test]
  public async Task TargetNameAttribute_ReturnsTargetNameSignature()
  {
    var text = "<Project>\n<Target Name=\"@CURSOR\">\n</Target>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNotNull();
    await Assert.That(sig!.Signatures[0].Label).Contains("target name");
  }

  [Test]
  public async Task UnknownAttribute_ReturnsNull()
  {
    var text = "<Project>\n<ItemGroup>\n<PackageReference Mystery=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (doc, line, character) = Pose(text);
    var sig = Sut.GetSignatureHelp(doc, line, character);
    await Assert.That(sig).IsNull();
  }
}