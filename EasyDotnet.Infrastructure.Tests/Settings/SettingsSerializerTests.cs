using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using EasyDotnet.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyDotnet.Infrastructure.Tests.Settings;

public class SettingsSerializerTests
{
  private readonly MockFileSystem _fileSystem;
  private readonly ILogger<SettingsSerializer> _logger;
  private readonly SettingsSerializer _serializer;
  private const string TestPath = "/settings/test.json";

  public SettingsSerializerTests()
  {
    _fileSystem = new MockFileSystem();
    _logger = NullLogger<SettingsSerializer>.Instance;
    _serializer = new SettingsSerializer(_fileSystem, _logger);
  }

  [Test]
  public async Task Read_FileDoesNotExist_ReturnsNull()
  {
    var result = _serializer.Read<SolutionSettings>(TestPath);

    await Assert.That(result).IsNull();
  }

  [Test]
  public async Task Read_ValidJson_ReturnsSettingsAndUpdateLastAccessed()
  {
    var originalTime = DateTime.UtcNow.AddDays(-1);
    var settings = new SolutionSettings() { Metadata = new() { OriginalPath = "/tmp" }, Defaults = new() { TestProject = "SomeTest" } };
    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    _fileSystem.AddFile(TestPath, new MockFileData(json));

    var result = _serializer.Read<SolutionSettings>(TestPath);

    await Assert.That(result).IsNotNull();
    await Assert.That(result?.Defaults?.TestProject).IsEqualTo("SomeTest");
    await Assert.That(result!.Metadata!.LastAccessed).IsGreaterThan(originalTime);

    var updatedJson = _fileSystem.File.ReadAllText(TestPath);
    await Assert.That(updatedJson).Contains("lastAccessed");
  }

  [Test]
  public async Task Read_CorruptedJson_DeletesFileAndReturnsNull()
  {
    _fileSystem.AddFile(TestPath, new MockFileData("invalid json {[["));

    var result = _serializer.Read<SolutionSettings>(TestPath);

    await Assert.That(result).IsNull();
    await Assert.That(_fileSystem.File.Exists(TestPath)).IsFalse();
  }
}