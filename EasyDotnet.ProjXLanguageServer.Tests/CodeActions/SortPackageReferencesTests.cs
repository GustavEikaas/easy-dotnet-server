using EasyDotnet.ProjXLanguageServer.Services;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EasyDotnet.ProjXLanguageServer.Tests.CodeActions;

public class SortPackageReferencesTests
{
  private static readonly CodeActionService Sut = new(new UserSecretsResolver(new System.IO.Abstractions.TestingHelpers.MockFileSystem()));

  private static LspRange WholeDocument(string text)
  {
    var lines = text.Split('\n');
    return new LspRange
    {
      Start = new Position { Line = 0, Character = 0 },
      End = new Position { Line = lines.Length - 1, Character = lines[^1].Length }
    };
  }

  private static string ApplyEdits(string original, TextEdit[] edits)
  {
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
  public async Task UnsortedPackageReferences_OffersSortAction()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Zebra\" Version=\"1.0.0\" />\n" +
        "    <PackageReference Include=\"Apple\" Version=\"2.0.0\" />\n" +
        "    <PackageReference Include=\"Mango\" Version=\"3.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    var actions = Sut.GetCodeActions(Docs.Make(text), WholeDocument(text), []);
    var sort = actions.Single(a => a.Title == "Sort PackageReferences alphabetically");
    var edits = sort.Edit!.Changes!.Values.Single();
    var result = ApplyEdits(text, edits);

    var apple = result.IndexOf("Apple", StringComparison.Ordinal);
    var mango = result.IndexOf("Mango", StringComparison.Ordinal);
    var zebra = result.IndexOf("Zebra", StringComparison.Ordinal);
    await Assert.That(apple < mango && mango < zebra).IsTrue();

    await Assert.That(result).Contains("Version=\"2.0.0\"");
    await Assert.That(result).Contains("Version=\"3.0.0\"");
    await Assert.That(result).Contains("Version=\"1.0.0\"");
  }

  [Test]
  public async Task AlreadySortedPackageReferences_OffersNoAction()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Apple\" Version=\"1.0.0\" />\n" +
        "    <PackageReference Include=\"Mango\" Version=\"2.0.0\" />\n" +
        "    <PackageReference Include=\"Zebra\" Version=\"3.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    var actions = Sut.GetCodeActions(Docs.Make(text), WholeDocument(text), []);
    await Assert.That(actions.Any(a => a.Title.StartsWith("Sort"))).IsFalse();
  }

  [Test]
  public async Task SinglePackageReference_OffersNoAction()
  {
    var text =
        "<Project>\n" +
        "  <ItemGroup>\n" +
        "    <PackageReference Include=\"Apple\" Version=\"1.0.0\" />\n" +
        "  </ItemGroup>\n" +
        "</Project>";

    var actions = Sut.GetCodeActions(Docs.Make(text), WholeDocument(text), []);
    await Assert.That(actions.Any(a => a.Title.StartsWith("Sort"))).IsFalse();
  }

  [Test]
  public async Task NoItemGroupAtCursor_OffersNoAction()
  {
    var text = "<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>";
    var actions = Sut.GetCodeActions(Docs.Make(text), WholeDocument(text), []);
    await Assert.That(actions.Any(a => a.Title.StartsWith("Sort"))).IsFalse();
  }
}