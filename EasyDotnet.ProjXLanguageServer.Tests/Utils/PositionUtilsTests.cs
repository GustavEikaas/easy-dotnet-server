using EasyDotnet.ProjXLanguageServer.Utils;

namespace EasyDotnet.ProjXLanguageServer.Tests.Utils;

public class PositionUtilsTests
{
  [Test]
  public async Task BuildLineOffsets_SingleLine_ReturnsZero()
  {
    var offsets = PositionUtils.BuildLineOffsets("abc");
    await Assert.That(offsets).IsEquivalentTo(new[] { 0 });
  }

  [Test]
  public async Task BuildLineOffsets_MultipleLines_ReturnsLineStarts()
  {
    var offsets = PositionUtils.BuildLineOffsets("ab\ncd\nef");
    await Assert.That(offsets).IsEquivalentTo(new[] { 0, 3, 6 });
  }

  [Test]
  public async Task ToOffset_LineCharacter_ConvertsCorrectly()
  {
    var text = "ab\ncd\nef";
    var offsets = PositionUtils.BuildLineOffsets(text);
    await Assert.That(PositionUtils.ToOffset(offsets, text.Length, 0, 0)).IsEqualTo(0);
    await Assert.That(PositionUtils.ToOffset(offsets, text.Length, 1, 1)).IsEqualTo(4);
    await Assert.That(PositionUtils.ToOffset(offsets, text.Length, 2, 2)).IsEqualTo(8);
  }

  [Test]
  public async Task ToOffset_BeyondEnd_ClampsToTextLength()
  {
    var text = "ab";
    var offsets = PositionUtils.BuildLineOffsets(text);
    await Assert.That(PositionUtils.ToOffset(offsets, text.Length, 5, 0)).IsEqualTo(text.Length);
  }
}