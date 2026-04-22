namespace EasyDotnet.ContainerTests.TestRunner.Fixtures;

/// <summary>
/// Test framework + adapter-protocol combination.
/// Each framework has a VSTest-era form and an MTP-era form; adapters are selected
/// server-side by <c>AdapterResolver</c> based on project properties, so the fixture
/// just has to produce the right package references.
/// </summary>
public enum TestFrameworkKind
{
  /// <summary>MSTest v2 via VSTest. Packages: Microsoft.NET.Test.Sdk + MSTest.TestAdapter + MSTest.TestFramework.</summary>
  MsTestVsTest,

  /// <summary>TUnit via MTP. OutputType=Exe, TestingPlatformDotnetTestSupport=true, PackageReference TUnit.</summary>
  TUnitMtp,

  /// <summary>xUnit v2 via VSTest. Matches EasyDotnet.IntegrationTests pinned versions (xunit 2.9.2 + xunit.runner.visualstudio 2.8.2).</summary>
  XUnitV2VsTest,

  /// <summary>xUnit v3 via MTP. Packages: xunit.v3 (meta) with OutputType=Exe.</summary>
  XUnitV3Mtp,

  /// <summary>NUnit v3 via VSTest. Packages: Microsoft.NET.Test.Sdk + NUnit 3.x + NUnit3TestAdapter.</summary>
  NUnitV3VsTest,

  /// <summary>NUnit v4 via MTP. NUnit 4.x + NUnit3TestAdapter 5.x + UseMicrosoftTestingPlatformRunner=true.</summary>
  NUnitV4Mtp,
}
