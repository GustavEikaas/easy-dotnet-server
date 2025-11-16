using FT = EasyDotnet.MsBuild.FileTypes;

namespace EasyDotnet.MsBuild.Tests.FileTypes;

public class FileTypesTests
{
  // -------- Solution files --------
  [Test]
  public async Task IsSolutionFile_ReturnsTrue_ForSln() => await Assert.That(FT.IsSolutionFile("MySolution.sln")).IsTrue();

  [Test]
  public async Task IsSolutionFile_ReturnsTrue_ForSln_Uppercase() => await Assert.That(FT.IsSolutionFile("MY.SOLUTION.SLN")).IsTrue();

  [Test]
  public async Task IsSolutionFile_ReturnsFalse_ForNonSln() => await Assert.That(FT.IsSolutionFile("Project.csproj")).IsFalse();

  [Test]
  public async Task IsSolutionXFile_ReturnsTrue_ForSlnx() => await Assert.That(FT.IsSolutionXFile("MySolution.slnx")).IsTrue();

  [Test]
  public async Task IsAnySolutionFile_ReturnsTrue_ForSlnOrSlnx()
  {
    await Assert.That(FT.IsAnySolutionFile("Project.sln")).IsTrue();
    await Assert.That(FT.IsAnySolutionFile("Project.slnx")).IsTrue();
  }

  [Test]
  public async Task IsAnySolutionFile_ReturnsFalse_ForOtherFiles() => await Assert.That(FT.IsAnySolutionFile("file.csproj")).IsFalse();

  // -------- Project files --------
  [Test]
  public async Task IsCsProjectFile_ReturnsTrue_ForCsproj() => await Assert.That(FT.IsCsProjectFile("Project.csproj")).IsTrue();

  [Test]
  public async Task IsFsProjectFile_ReturnsTrue_ForFsproj() => await Assert.That(FT.IsFsProjectFile("Project.fsproj")).IsTrue();

  [Test]
  public async Task IsAnyProjectFile_ReturnsTrue_ForCsprojOrFsproj()
  {
    await Assert.That(FT.IsAnyProjectFile("Project.csproj")).IsTrue();
    await Assert.That(FT.IsAnyProjectFile("Project.fsproj")).IsTrue();
  }

  [Test]
  public async Task IsAnyProjectFile_ReturnsFalse_ForOtherFiles() => await Assert.That(FT.IsAnyProjectFile("file.txt")).IsFalse();

  // -------- Source files --------
  [Test]
  public async Task IsCsFile_ReturnsTrue_ForCsFile() => await Assert.That(FT.IsCsFile("Program.cs")).IsTrue();

  [Test]
  public async Task IsFsFile_ReturnsTrue_ForFsFile() => await Assert.That(FT.IsFsFile("Module.fs")).IsTrue();

  [Test]
  public async Task SourceFileChecks_ReturnFalse_ForWrongExtensions()
  {
    await Assert.That(FT.IsCsFile("file.fs")).IsFalse();
    await Assert.That(FT.IsFsFile("file.cs")).IsFalse();
  }

  // -------- Edge cases --------
  [Test]
  public async Task Methods_ReturnFalse_ForEmptyOrNullPaths()
  {
    await Assert.That(FT.IsSolutionFile("")).IsFalse();
    await Assert.That(FT.IsSolutionXFile("")).IsFalse();
    await Assert.That(FT.IsAnySolutionFile("")).IsFalse();
    await Assert.That(FT.IsCsProjectFile("")).IsFalse();
    await Assert.That(FT.IsFsProjectFile("")).IsFalse();
    await Assert.That(FT.IsAnyProjectFile("")).IsFalse();
    await Assert.That(FT.IsCsFile("")).IsFalse();
    await Assert.That(FT.IsFsFile("")).IsFalse();

    await Assert.That(FT.IsSolutionFile(null!)).IsFalse();
    await Assert.That(FT.IsSolutionXFile(null!)).IsFalse();
    await Assert.That(FT.IsAnySolutionFile(null!)).IsFalse();
    await Assert.That(FT.IsCsProjectFile(null!)).IsFalse();
    await Assert.That(FT.IsFsProjectFile(null!)).IsFalse();
    await Assert.That(FT.IsAnyProjectFile(null!)).IsFalse();
    await Assert.That(FT.IsCsFile(null!)).IsFalse();
    await Assert.That(FT.IsFsFile(null!)).IsFalse();
  }
}