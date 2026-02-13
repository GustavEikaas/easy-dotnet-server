using System.IO.Abstractions.TestingHelpers;
using EasyDotnet.Infrastructure.Settings;

namespace EasyDotnet.Infrastructure.Tests.Settings;

public class SettingsFileResolverTests : IDisposable
{
  private readonly MockFileSystem _mockFileSystem;
  private readonly string _tempDirectory = "/test/settings";
  private readonly SettingsFileResolver _resolver;
  public string LongPath;
  public string App1Sln = "/home/user/project/App1.sln";
  public string App2Sln = "/home/user/project/App2.sln";
  public string MyAppSln = "/home/user/project/MyApp.sln";
  public string MyAppCsproj = "/home/user/project/MyApp.csproj";
  public string UnicodeSln = "/home/user/项目/MyApp.sln";

  public SettingsFileResolverTests()
  {
    var longSegment = new string('a', 100);
    var longPath = $"/home/user/{longSegment}/{longSegment}/{longSegment}/MyApp.sln";
    LongPath = longPath;
    _mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { longPath, new MockFileData("") },
            { MyAppSln, new MockFileData("") },
            { App1Sln, new MockFileData("") },
            { App2Sln, new MockFileData("") },
            { MyAppCsproj, new MockFileData("") },
            { UnicodeSln, new MockFileData("") }
        });
    _resolver = new SettingsFileResolver(_mockFileSystem, _tempDirectory);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDirectory))
    {
      Directory.Delete(_tempDirectory, recursive: true);
    }
  }

  [Test]
  public async Task HashDeterminism_SamePathProducesSameHash()
  {
    var hash1 = _resolver.GetSettingsFilePath(MyAppSln, SettingsScope.Solution);
    var hash2 = _resolver.GetSettingsFilePath(MyAppSln, SettingsScope.Solution);

    await Assert.That(hash1).IsEqualTo(hash2);
  }

  [Test]
  public async Task HashDeterminism_DifferentPathsProduceDifferentHashes()
  {
    var hash1 = _resolver.GetSettingsFilePath(App1Sln, SettingsScope.Solution);
    var hash2 = _resolver.GetSettingsFilePath(App2Sln, SettingsScope.Solution);

    await Assert.That(hash1).IsNotEqualTo(hash2);
  }

  [Test]
  public async Task GetSettingsFilePath_SolutionScopeHasCorrectFormat()
  {
    var filePath = _resolver.GetSettingsFilePath(MyAppSln, SettingsScope.Solution);
    var fileName = Path.GetFileName(filePath);

    await Assert.That(fileName).Matches(@"^solution_[a-f0-9]{32}\.json$");
  }

  [Test]
  public async Task GetSettingsFilePath_ProjectScopeHasCorrectFormat()
  {
    var filePath = _resolver.GetSettingsFilePath(MyAppCsproj, SettingsScope.Project);
    var fileName = Path.GetFileName(filePath);

    await Assert.That(fileName).Matches(@"^project_[a-f0-9]{32}\.json$");
  }

  [Test]
  public async Task GetSettingsFilePath_ReturnsPathInConfiguredDirectory()
  {
    var filePath = _resolver.GetSettingsFilePath(MyAppSln, SettingsScope.Solution);

    await Assert.That(filePath).StartsWith(_tempDirectory);
  }

  [Test]
  public async Task GetAllSettingsFiles_ReturnsOnlySolutionFiles()
  {
    var solutionFile = Path.Combine(_tempDirectory, "solution_abc123.json");
    var projectFile = Path.Combine(_tempDirectory, "project_def456.json");
    var otherFile = Path.Combine(_tempDirectory, "other.json");

    _mockFileSystem.File.WriteAllText(solutionFile, "{}");
    _mockFileSystem.File.WriteAllText(projectFile, "{}");
    _mockFileSystem.File.WriteAllText(otherFile, "{}");

    var files = _resolver.GetAllSettingsFiles(SettingsScope.Solution).ToList();

    await Assert.That(files.Count).IsEqualTo(1);
    await Assert.That(files[0]).IsEqualTo(_mockFileSystem.Path.GetFullPath(solutionFile));
  }

  [Test]
  public async Task GetAllSettingsFiles_ReturnsOnlyProjectFiles()
  {
    var solutionFile = Path.Combine(_tempDirectory, "solution_abc123.json");
    var projectFile = Path.Combine(_tempDirectory, "project_def456.json");
    var otherFile = Path.Combine(_tempDirectory, "other.json");

    _mockFileSystem.File.WriteAllText(solutionFile, "{}");
    _mockFileSystem.File.WriteAllText(projectFile, "{}");
    _mockFileSystem.File.WriteAllText(otherFile, "{}");

    var files = _resolver.GetAllSettingsFiles(SettingsScope.Project).ToList();

    await Assert.That(files.Count).IsEqualTo(1);
    await Assert.That(files[0]).IsEqualTo(_mockFileSystem.Path.GetFullPath(projectFile));
  }

  [Test]
  public async Task GetAllSettingsFiles_ReturnsMultipleMatchingFiles()
  {
    var file1 = Path.Combine(_tempDirectory, "solution_abc123.json");
    var file2 = Path.Combine(_tempDirectory, "solution_def456.json");
    var file3 = Path.Combine(_tempDirectory, "solution_ghi789.json");

    _mockFileSystem.File.WriteAllText(file1, "{}");
    _mockFileSystem.File.WriteAllText(file2, "{}");
    _mockFileSystem.File.WriteAllText(file3, "{}");

    var files = _resolver.GetAllSettingsFiles(SettingsScope.Solution).ToList();

    await Assert.That(files.Count).IsEqualTo(3);
  }

  [Test]
  public async Task DirectoryCreation_CreatesDirectoryIfNotExists()
  {
    var newDirectory = _mockFileSystem.Path.Combine(Path.GetTempPath(), $"settings-new-{Guid.NewGuid()}");

    await Assert.That(_mockFileSystem.Directory.Exists(newDirectory)).IsFalse();
    _ = new SettingsFileResolver(_mockFileSystem, newDirectory);

    await Assert.That(_mockFileSystem.Directory.Exists(newDirectory)).IsTrue();
  }

  [Test]
  public async Task DirectoryCreation_DoesNotFailWhenDirectoryAlreadyExists()
  {
    await Assert.That(_mockFileSystem.Directory.Exists(_tempDirectory)).IsTrue();

    _ = new SettingsFileResolver(_mockFileSystem, _tempDirectory);
    await Assert.That(_mockFileSystem.Directory.Exists(_tempDirectory)).IsTrue();
  }

  [Test]
  public async Task InvalidPaths_VeryLongPathHashesCorrectly()
  {
    var filePath = _resolver.GetSettingsFilePath(LongPath, SettingsScope.Solution);

    await Assert.That(filePath).IsNotNull();
    await Assert.That(_mockFileSystem.Path.GetFileName(filePath)).Matches(@"^solution_[a-f0-9]{32}\.json$");

    var fileName = _mockFileSystem.Path.GetFileNameWithoutExtension(filePath);
    var hash = fileName.Replace("solution_", "");
    await Assert.That(hash.Length).EqualTo(32);
  }

  [Test]
  public async Task InvalidPaths_PathWithUnicodeCharactersHashesCorrectly()
  {
    var filePath = _resolver.GetSettingsFilePath(UnicodeSln, SettingsScope.Solution);

    await Assert.That(filePath).IsNotNull();
    await Assert.That(Path.GetFileName(filePath)).Matches(@"^solution_[a-f0-9]{32}\.json$");
  }

  [Test]
  public async Task InvalidPaths_DotSegmentsAreNormalized()
  {
    var relPath = "/home/user/project/../project/MyApp.sln";

    var hash1 = _resolver.GetSettingsFilePath(MyAppSln, SettingsScope.Solution);
    var hash2 = _resolver.GetSettingsFilePath(relPath, SettingsScope.Solution);

    await Assert.That(Path.GetFileName(hash1)).IsEqualTo(Path.GetFileName(hash2));
  }
}