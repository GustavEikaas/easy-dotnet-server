using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using EasyDotnet.ProjXLanguageServer.Utils;

namespace EasyDotnet.ProjXLanguageServer.Tests.Completion;

public class PartialTagCompletionTests
{
  private static readonly CompletionService Sut = new();

  private static (CursorContextKind kind, string? element, string? parent) ResolveAt(string text)
  {
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);
    var ctx = XmlContextResolver.Resolve(Docs.Make(clean), line, character);
    return (ctx.Kind, ctx.ElementName, ctx.ParentElementName);
  }

  private static EasyDotnet.ProjXLanguageServer.Services.CsprojDocument DocAt(string text, out int line, out int character)
  {
    (line, character) = Docs.PositionAt(text, "@CURSOR");
    return Docs.Make(text.Replace("@CURSOR", string.Empty));
  }

  [Test]
  public async Task EndOfFile_AfterPartialTag_NoClosingTags_OffersPropertyCompletions()
  {
    var text = "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n\n    <PropertyGroup>\n    <T@CURSOR";
    var doc = DocAt(text, out var line, out var character);
    var items = Sut.GetCompletions(doc, line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task PartialTag_PropertyGroupClosed_ProjectOpen_OffersPropertyCompletions()
  {
    var text = "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <PropertyGroup>\n    <T@CURSOR\n  </PropertyGroup>";
    var doc = DocAt(text, out var line, out var character);
    var items = Sut.GetCompletions(doc, line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task PartialTag_NoClosingPropertyGroup_OffersPropertyCompletions()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <T@CURSOR\n</Project>";
    var doc = DocAt(text, out var line, out var character);
    var items = Sut.GetCompletions(doc, line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task PartialTag_AfterExistingProperty_OffersPropertyCompletions()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n    <T@CURSOR\n  </PropertyGroup>\n</Project>";
    var doc = DocAt(text, out var line, out var character);
    var items = Sut.GetCompletions(doc, line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task PartialTag_WithSpaceAfter_OffersPropertyCompletions()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <T @CURSOR\n  </PropertyGroup>\n</Project>";
    var doc = DocAt(text, out var line, out var character);
    var items = Sut.GetCompletions(doc, line, character);
    await Assert.That(items.Any(i => i.Label == "TargetFramework")).IsTrue();
  }

  [Test]
  public async Task ResolveContext_EndOfFile_PartialTag_NotUnknown()
  {
    var text = "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n\n    <PropertyGroup>\n    <T@CURSOR";
    var (kind, element, parent) = ResolveAt(text);
    var summary = $"kind={kind} element={element ?? "<null>"} parent={parent ?? "<null>"}";
    await Assert.That(kind).IsNotEqualTo(CursorContextKind.Unknown);
    await Assert.That(summary).Contains("PropertyGroup");
  }
}