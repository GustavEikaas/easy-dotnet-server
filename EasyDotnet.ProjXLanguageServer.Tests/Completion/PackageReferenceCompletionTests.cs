using EasyDotnet.Nuget;
using EasyDotnet.ProjXLanguageServer.Tests.Helpers;
using NuGet.Versioning;

namespace EasyDotnet.ProjXLanguageServer.Tests.Completion;

public class PackageReferenceCompletionTests
{
  private static (int line, int character, string clean) PositionFor(string text)
  {
    var (line, character) = Docs.PositionAt(text, "@CURSOR");
    return (line, character, text.Replace("@CURSOR", string.Empty));
  }

  [Test]
  public async Task IncludeAttribute_ReturnsHitsFromNugetService()
  {
    var fake = new FakeNugetSearchService
    {
      OnSearch = term =>
      [
        new NugetPackageHit("nuget.org", "Newtonsoft.Json", "13.0.3", "JSON framework", 1_000_000_000, "James Newton-King"),
        new NugetPackageHit("nuget.org", "NewtonSoft.Whatever", "1.0.0", null, 100, null),
      ]
    };
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"Newt@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(fake.SearchCalls).IsEqualTo(1);
    await Assert.That(result.IsIncomplete).IsTrue();
    await Assert.That(result.Items.Any(i => i.Label == "Newtonsoft.Json")).IsTrue();
  }

  [Test]
  public async Task IncludeAttribute_EmptyPrefix_DoesNotCallNuget()
  {
    var fake = new FakeNugetSearchService();
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(fake.SearchCalls).IsEqualTo(0);
    await Assert.That(result.Items.Length).IsEqualTo(0);
  }

  [Test]
  public async Task IncludeAttribute_WhenNugetThrows_ReturnsEmpty()
  {
    var fake = new FakeNugetSearchService { ShouldThrow = true };
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"Newt@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items.Length).IsEqualTo(0);
  }

  [Test]
  public async Task VersionAttribute_UsesIncludeAsPackageId()
  {
    string? capturedId = null;
    var fake = new FakeNugetSearchService
    {
      OnVersions = id =>
      {
        capturedId = id;
        return [NuGetVersion.Parse("13.0.3"), NuGetVersion.Parse("12.0.3")];
      }
    };
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"Newtonsoft.Json\" Version=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(capturedId).IsEqualTo("Newtonsoft.Json");
    await Assert.That(result.Items.Any(i => i.Label == "13.0.3")).IsTrue();
    await Assert.That(result.Items[0].Label).IsEqualTo("latest");
    await Assert.That(result.Items[0].InsertText).IsEqualTo("13.0.3");
  }

  [Test]
  public async Task VersionAttribute_AddsLatestPreviewWhenPrereleaseIsNewer()
  {
    var fake = new FakeNugetSearchService
    {
      OnVersions = _ =>
      [
        NuGetVersion.Parse("14.0.0-preview1"),
        NuGetVersion.Parse("13.0.3"),
        NuGetVersion.Parse("12.0.3"),
      ]
    };
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"Newtonsoft.Json\" Version=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items[0].Label).IsEqualTo("latest");
    await Assert.That(result.Items[0].InsertText).IsEqualTo("13.0.3");
    await Assert.That(result.Items[1].Label).IsEqualTo("latest-preview");
    await Assert.That(result.Items[1].InsertText).IsEqualTo("14.0.0-preview1");
  }

  [Test]
  public async Task VersionAttribute_OnlyPrereleaseVersions_AddsLatestPreviewOnly()
  {
    var fake = new FakeNugetSearchService
    {
      OnVersions = _ =>
      [
        NuGetVersion.Parse("1.0.0-beta2"),
        NuGetVersion.Parse("1.0.0-beta1"),
      ]
    };
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Include=\"Foo\" Version=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(result.Items.Any(i => i.Label == "latest")).IsFalse();
    await Assert.That(result.Items[0].Label).IsEqualTo("latest-preview");
    await Assert.That(result.Items[0].InsertText).IsEqualTo("1.0.0-beta2");
  }

  [Test]
  public async Task VersionAttribute_WithoutInclude_DoesNotCallNuget()
  {
    var fake = new FakeNugetSearchService();
    var sut = CompletionTestFactory.Create(fake);

    var text = "<Project>\n<ItemGroup>\n<PackageReference Version=\"@CURSOR\" />\n</ItemGroup>\n</Project>";
    var (line, character, clean) = PositionFor(text);

    var result = await sut.GetCompletionsAsync(Docs.Make(clean), line, character, default);

    await Assert.That(fake.VersionCalls).IsEqualTo(0);
    await Assert.That(result.Items.Length).IsEqualTo(0);
  }
}