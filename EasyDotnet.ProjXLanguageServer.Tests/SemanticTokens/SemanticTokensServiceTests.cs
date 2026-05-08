using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using EasyDotnet.ProjXLanguageServer.Utils;

namespace EasyDotnet.ProjXLanguageServer.Tests.SemanticTokens;

public class SemanticTokensServiceTests
{
  private static readonly SemanticTokensService Sut = new();

  private static List<DecodedToken> Decode(int[] data, string text)
  {
    var lineOffsets = PositionUtils.BuildLineOffsets(text);
    var result = new List<DecodedToken>();
    var line = 0;
    var character = 0;
    for (var i = 0; i < data.Length; i += 5)
    {
      var deltaLine = data[i];
      var deltaChar = data[i + 1];
      var length = data[i + 2];
      var typeIdx = data[i + 3];
      if (deltaLine == 0)
        character += deltaChar;
      else
      {
        line += deltaLine;
        character = deltaChar;
      }
      var offset = lineOffsets[line] + character;
      result.Add(new DecodedToken(line, character, length, typeIdx, text.Substring(offset, length)));
    }
    return result;
  }

  [Test]
  public async Task Tokens_HighlightsElementNamesAsClass()
  {
    var text = "<Project>\n<PropertyGroup>\n<Nullable>enable</Nullable>\n</PropertyGroup>\n</Project>";
    var tokens = Decode(Sut.GetTokens(Docs.Make(text)).Data!, text);
    await Assert.That(tokens.Any(t => t.Text == "Project" && t.TypeIndex == 0)).IsTrue();
    await Assert.That(tokens.Any(t => t.Text == "PropertyGroup" && t.TypeIndex == 0)).IsTrue();
    await Assert.That(tokens.Any(t => t.Text == "Nullable" && t.TypeIndex == 0)).IsTrue();
  }

  [Test]
  public async Task Tokens_HighlightsUserSecretsIdGuid_AsEnumMember()
  {
    var guid = "55555555-5555-5555-5555-555555555555";
    var text = $"<Project><PropertyGroup><UserSecretsId>{guid}</UserSecretsId></PropertyGroup></Project>";
    var tokens = Decode(Sut.GetTokens(Docs.Make(text)).Data!, text);
    var guidToken = tokens.FirstOrDefault(t => t.Text == guid);
    await Assert.That(guidToken).IsNotNull();
    await Assert.That(guidToken!.TypeIndex).IsEqualTo(4);
  }

  [Test]
  public async Task Tokens_HighlightsAttributeNameAsProperty_AndPackageVersionAsNumber()
  {
    var text = "<Project><ItemGroup><PackageReference Include=\"X\" Version=\"1.2.3\" /></ItemGroup></Project>";
    var tokens = Decode(Sut.GetTokens(Docs.Make(text)).Data!, text);
    await Assert.That(tokens.Any(t => t.Text == "Include" && t.TypeIndex == 1)).IsTrue();
    await Assert.That(tokens.Any(t => t.Text == "Version" && t.TypeIndex == 1)).IsTrue();
    await Assert.That(tokens.Any(t => t.Text == "1.2.3" && t.TypeIndex == 3)).IsTrue();
  }

  [Test]
  public async Task Tokens_HighlightsMsBuildVarsAsMacro_NonOverlapping()
  {
    var text = "<Project><PropertyGroup><OutputPath>bin/$(Configuration)/end</OutputPath></PropertyGroup></Project>";
    var tokens = Decode(Sut.GetTokens(Docs.Make(text)).Data!, text);

    var macroToken = tokens.FirstOrDefault(t => t.Text == "$(Configuration)");
    await Assert.That(macroToken).IsNotNull();
    await Assert.That(macroToken!.TypeIndex).IsEqualTo(5);

    await Assert.That(tokens.Any(t => t.Text == "bin/" && t.TypeIndex == 2)).IsTrue();
    await Assert.That(tokens.Any(t => t.Text == "/end" && t.TypeIndex == 2)).IsTrue();

    var sorted = tokens.OrderBy(t => t.Line).ThenBy(t => t.Char).ToList();
    for (var i = 1; i < sorted.Count; i++)
    {
      var prev = sorted[i - 1];
      var prevEnd = prev.Char + prev.Length;
      if (sorted[i].Line == prev.Line)
        await Assert.That(sorted[i].Char >= prevEnd).IsTrue();
    }
  }

  [Test]
  public async Task Tokens_DeltaEncoded_NoNegativeDeltas()
  {
    var text = "<Project>\n<PropertyGroup>\n<TargetFramework>net8.0</TargetFramework>\n</PropertyGroup>\n</Project>";
    var data = Sut.GetTokens(Docs.Make(text)).Data!;
    for (var i = 0; i < data.Length; i += 5)
    {
      var deltaLine = data[i];
      var deltaChar = data[i + 1];
      await Assert.That(deltaLine >= 0).IsTrue();
      if (deltaLine == 0)
        await Assert.That(deltaChar >= 0).IsTrue();
    }
  }

  private record DecodedToken(int Line, int Char, int Length, int TypeIndex, string Text);
}