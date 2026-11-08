using EasyDotnet.IDE.Commands;
using EasyDotnet.IDE.Services;

namespace EasyDotnet.IDE.Tests.Roslyn;

[NotInParallel]
public class RoslynLocatorTests
{

  [Test]
  public async Task TryParseVersion_StripsBuildMetadata()
  {
    var version = RoslynToolService.TryParseVersion("5.8.0-1.26252.1+3d098b3a2f24112aa06731d38ea6dd7334169998");
    await Assert.That(version).IsNotNull();
    await Assert.That(version!.ToString()).IsEqualTo("5.8.0-1.26252.1");
  }

  [Test]
  public async Task IsBelowRecommendedVersion_UsesMinimumRecommendedVersion()
  {
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.12.0-1.26475.2")).IsTrue();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.13.0-1.26507.3")).IsTrue();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.13.0-1.26507.4")).IsFalse();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.13.0-1.26514.10")).IsFalse();
  }

  [Test]
  public async Task BuildRoslynArguments_EasyDotnetAnalyzer_DoesNotEnableDevKitDependency()
  {
    Directory.CreateDirectory(Path.Combine(GetRoslynBaseDirForTests(), "Analyzers"));

    var arguments = RoslynStartCommand.BuildRoslynArguments(
        new RoslynStartCommand.Settings { UseEasyDotnetAnalyzer = true },
        "/tmp/easydotnet-roslyn-logs");

    await Assert.That(arguments.Contains("--devKitDependencyPath")).IsFalse();
  }

  [Test]
  public async Task BuildRoslynArguments_EasyDotnetExtension_AddsBundledExtensionWithoutDevKitDependency()
  {
    var extensionPath = Path.Combine(GetRoslynBaseDirForTests(), "Extensions", "EasyDotnet", "EasyDotnet.RoslynLanguageServices.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(extensionPath)!);
    await File.WriteAllTextAsync(extensionPath, string.Empty);

    var arguments = RoslynStartCommand.BuildRoslynArguments(
        new RoslynStartCommand.Settings { UseEasyDotnetExtension = true },
        "/tmp/easydotnet-roslyn-logs");

    await Assert.That(arguments.Contains(extensionPath)).IsTrue();
    await Assert.That(arguments.Contains("--devKitDependencyPath")).IsFalse();
  }

  [After(Test)]
  public void Cleanup()
  {
    Environment.SetEnvironmentVariable(WellKnownEnvironment.RoslynPath.Name, null);

    var toolsDir = Path.Combine(GetTestProjectDir(), "tools");
    if (Directory.Exists(toolsDir))
    {
      Directory.Delete(toolsDir, recursive: true);
    }
  }

  private static string GetRoslynBaseDirForTests()
  {
    var assemblyLocation = typeof(RoslynLocator).Assembly.Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");

    return Path.Combine(GetTestProjectDir(), "tools", "Roslyn");
  }

  private static string GetTestProjectDir()
  {
    var assemblyLocation = typeof(RoslynLocator).Assembly.Location;
    var toolExeDir = Path.GetDirectoryName(assemblyLocation)
                     ?? throw new InvalidOperationException("Unable to determine assembly directory");

    return Path.GetFullPath(Path.Combine(toolExeDir, "..", "..", ".."));
  }
}