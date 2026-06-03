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
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26252.1")).IsTrue();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26262.10")).IsFalse();
    await Assert.That(RoslynToolService.IsBelowRecommendedVersion("5.8.0-1.26266.2")).IsFalse();
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
  public async Task BuildRoslynArguments_EasyDotnetExtension_AddsBundledExtensionAndDevKitDependency()
  {
    var roslynBaseDir = GetRoslynBaseDirForTests();
    var extensionPath = Path.Combine(roslynBaseDir, "Extensions", "EasyDotnet", "EasyDotnet.RoslynLanguageServices.dll");
    var devKitPath = Path.Combine(roslynBaseDir, "DevKit", "Microsoft.CodeAnalysis.ExternalAccess.Extensions.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(extensionPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(devKitPath)!);
    await File.WriteAllTextAsync(extensionPath, string.Empty);
    await File.WriteAllTextAsync(devKitPath, string.Empty);

    var arguments = RoslynStartCommand.BuildRoslynArguments(
        new RoslynStartCommand.Settings { UseEasyDotnetExtension = true },
        "/tmp/easydotnet-roslyn-logs");

    await Assert.That(arguments.Contains(extensionPath)).IsTrue();
    await Assert.That(arguments.Contains("--devKitDependencyPath")).IsTrue();
    await Assert.That(arguments.Contains(devKitPath)).IsTrue();
  }

  [After(Test)]
  public void Cleanup()
  {
    Environment.SetEnvironmentVariable(RoslynLocator.ROSLYN_DLL_PATH_ENV, null);

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