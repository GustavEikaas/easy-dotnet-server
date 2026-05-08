using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.ProjXLanguageServer.Services;

namespace EasyDotnet.ProjXLanguageServer.Tests.Definition;

public class UserSecretsResolverTests
{
  [Test]
  public async Task EnsureSecretsFile_CreatesDirectoryAndFile_WhenMissing()
  {
    var fs = new MockFileSystem();
    var sut = new UserSecretsResolver(fs);
    var path = sut.EnsureSecretsFile("11111111-1111-1111-1111-111111111111");

    await Assert.That(fs.File.Exists(path)).IsTrue();
    await Assert.That(fs.File.ReadAllText(path)).IsEqualTo("{}");
  }

  [Test]
  public async Task EnsureSecretsFile_LeavesFile_WhenExisting()
  {
    var fs = new MockFileSystem();
    var sut = new UserSecretsResolver(fs);

    var first = sut.EnsureSecretsFile("22222222-2222-2222-2222-222222222222");
    fs.File.WriteAllText(first, "{\"k\":\"v\"}");

    var again = sut.EnsureSecretsFile("22222222-2222-2222-2222-222222222222");
    await Assert.That(again).IsEqualTo(first);
    await Assert.That(fs.File.ReadAllText(again)).IsEqualTo("{\"k\":\"v\"}");
  }

  [Test]
  public async Task GetSecretsPath_BuildsCanonicalPath()
  {
    var fs = new MockFileSystem();
    var sut = new UserSecretsResolver(fs);
    var path = sut.GetSecretsPath("33333333-3333-3333-3333-333333333333");
    await Assert.That(path).EndsWith("secrets.json");
    await Assert.That(path).Contains("33333333-3333-3333-3333-333333333333");
    await Assert.That(path.Contains("usersecrets", StringComparison.OrdinalIgnoreCase)).IsTrue();
  }
}