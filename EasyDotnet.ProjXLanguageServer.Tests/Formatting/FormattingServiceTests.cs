using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EasyDotnet.ProjXLanguageServer.Tests.Formatting;

public class FormattingServiceTests
{
  private static readonly FormattingService Sut = new();
  private static readonly FormattingOptions Opts = new() { TabSize = 2, InsertSpaces = true };

  private static string Apply(string original, TextEdit[] edits)
  {
    if (edits.Length == 0)
      return original;
    var doc = Docs.Make(original);
    var ordered = edits
        .Select(e => (start: doc.ToOffset(e.Range.Start.Line, e.Range.Start.Character),
                       end: doc.ToOffset(e.Range.End.Line, e.Range.End.Character),
                       text: e.NewText))
        .OrderByDescending(e => e.start)
        .ToList();
    var result = original;
    foreach (var (start, end, text) in ordered)
      result = string.Concat(result.AsSpan(0, start), text, result.AsSpan(end));
    return result;
  }

  [Test]
  public async Task ReindentsMessyDocument()
  {
    var input =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "<PropertyGroup>\n" +
        "<TargetFramework>net8.0</TargetFramework>\n" +
        "<Nullable>enable</Nullable>\n" +
        "</PropertyGroup>\n" +
        "<ItemGroup>\n" +
        "<PackageReference Include=\"Foo\" Version=\"1.0.0\" />\n" +
        "</ItemGroup>\n" +
        "</Project>";
    var expected =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "  </PropertyGroup>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Foo\" Version=\"1.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>\n";

    var edits = Sut.Format(Docs.Make(input), Opts);
    var result = Apply(input, edits);
    await Assert.That(result).IsEqualTo(expected);
  }

  [Test]
  public async Task IsIdempotent()
  {
    var input =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net8.0</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "</Project>\n";
    var edits = Sut.Format(Docs.Make(input), Opts);
    await Assert.That(edits.Length).IsEqualTo(0);
  }

  [Test]
  public async Task PreservesComments()
  {
    var input =
        "<Project>\n" +
        "<!-- top comment -->\n" +
        "<PropertyGroup>\n" +
        "<!-- inner comment -->\n" +
        "<Nullable>enable</Nullable>\n" +
        "</PropertyGroup>\n" +
        "</Project>";
    var edits = Sut.Format(Docs.Make(input), Opts);
    var result = Apply(input, edits);
    await Assert.That(result).Contains("<!-- top comment -->");
    await Assert.That(result).Contains("<!-- inner comment -->");
  }

  [Test]
  public async Task BailsOnMalformedDocument()
  {
    var input = "<Project>\n  <PropertyGroup>\n    <TargetFramework>net8.0\n  </PropertyGroup>\n</Project>";
    var edits = Sut.Format(Docs.Make(input), Opts);
    await Assert.That(edits.Length).IsEqualTo(0);
  }

  [Test]
  public async Task TrimsInnerTextWhitespace()
  {
    var input = "<Project><PropertyGroup><TargetFramework>   net8.0   </TargetFramework></PropertyGroup></Project>";
    var edits = Sut.Format(Docs.Make(input), Opts);
    var result = Apply(input, edits);
    await Assert.That(result).Contains("<TargetFramework>net8.0</TargetFramework>");
  }

  [Test]
  public async Task PreservesSingleBlankLinesBetweenSiblings()
  {
    var input =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "\n" +
        "  <PropertyGroup>\n" +
        "    <TargetFramework>net10.0</TargetFramework>\n" +
        "  </PropertyGroup>\n" +
        "\n" +
        "  <PropertyGroup>\n" +
        "    <PackageId>Foo</PackageId>\n" +
        "  </PropertyGroup>\n" +
        "\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Serilog\" Version=\"5.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "\n" +
        "</Project>";
    var edits = Sut.Format(Docs.Make(input), Opts);
    var result = Apply(input, edits);
    await Assert.That(result).Contains("</PropertyGroup>\n\n  <PropertyGroup>");
    await Assert.That(result).Contains("</PropertyGroup>\n\n  <ItemGroup>");
  }

  [Test]
  public async Task CollapsesMultipleBlankLinesToOne()
  {
    var input =
        "<Project>\n" +
        "  <PropertyGroup>\n" +
        "    <Nullable>enable</Nullable>\n" +
        "  </PropertyGroup>\n" +
        "\n" +
        "\n" +
        "\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"X\" Version=\"1\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";
    var edits = Sut.Format(Docs.Make(input), Opts);
    var result = Apply(input, edits);
    await Assert.That(result).Contains("</PropertyGroup>\n\n  <ItemGroup>");
    await Assert.That(result).DoesNotContain("\n\n\n");
  }

  [Test]
  public async Task UsesTabsWhenRequested()
  {
    var input = "<Project>\n<PropertyGroup>\n<Nullable>enable</Nullable>\n</PropertyGroup>\n</Project>";
    var opts = new FormattingOptions { TabSize = 4, InsertSpaces = false };
    var edits = Sut.Format(Docs.Make(input), opts);
    var result = Apply(input, edits);
    await Assert.That(result).Contains("\t<PropertyGroup>");
    await Assert.That(result).Contains("\t\t<Nullable>enable</Nullable>");
  }
}