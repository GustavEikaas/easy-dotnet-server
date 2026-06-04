using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;

namespace EasyDotnet.ProjXLanguageServer.Tests.Completion;

public class InlineCompletionServiceTests
{
  private static readonly InlineCompletionService Sut = new(CompletionTestFactory.Create());

  [Test]
  public async Task PartialPropertyTag_OffersPlainInnerAndEndTag()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFrame@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var result = await Sut.GetInlineCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items.Length).IsEqualTo(1);
    await Assert.That(result.Items[0].InsertText).IsEqualTo("work></TargetFramework>");
  }

  [Test]
  public async Task FullPropertyTag_OffersClosingText()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework@CURSOR\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var result = await Sut.GetInlineCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items.Length).IsEqualTo(1);
    await Assert.That(result.Items[0].InsertText).IsEqualTo("></TargetFramework>");
  }

  [Test]
  public async Task ElementValue_DoesNotOfferInlineTagCompletion()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>net@CURSOR</TargetFramework>\n</PropertyGroup>\n</Project>";
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    var clean = text.Replace("@CURSOR", string.Empty);

    var result = await Sut.GetInlineCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items.Length).IsEqualTo(0);
  }
}
