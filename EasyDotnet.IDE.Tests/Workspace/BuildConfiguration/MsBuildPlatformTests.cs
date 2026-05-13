using EasyDotnet.IDE.Workspace.BuildConfiguration;

namespace EasyDotnet.IDE.Tests.Workspace.BuildConfiguration;

public sealed class MsBuildPlatformTests
{
  [Test]
  [Arguments(null)]
  [Arguments("")]
  [Arguments("   ")]
  [Arguments("Any CPU")]
  [Arguments("any cpu")]
  [Arguments("AnyCPU")]
  [Arguments("anycpu")]
  [Arguments("  Any CPU  ")]
  public async Task ToProjectPlatform_AnyCpuVariants_ReturnNull(string? input)
  {
    await Assert.That(MsBuildPlatform.ToProjectPlatform(input)).IsNull();
  }

  [Test]
  [Arguments("x64", "x64")]
  [Arguments("x86", "x86")]
  [Arguments("ARM64", "ARM64")]
  [Arguments("  x64  ", "x64")]
  public async Task ToProjectPlatform_NonDefaultPlatform_PassesThrough(string input, string expected)
  {
    await Assert.That(MsBuildPlatform.ToProjectPlatform(input)).IsEqualTo(expected);
  }
}
