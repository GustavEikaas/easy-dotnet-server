using System;
using System.IO;
using System.Linq;
using EasyDotnet.VSTest;

namespace EasyDotnet.Services;

public class VsTestService(OutFileWriterService outFileWriter, LogService logService)
{
  public void RunDiscover(DiscoverProjectRequest[] projects)
  {
    var vsTestPath = GetVsTestPath();
    logService.Info($"Using VSTest path: {vsTestPath}");
    var dllPaths = projects.Select(x => x.DllPath).ToArray();
    var discoveredTests = DiscoverHandler.Discover(vsTestPath, dllPaths);

    projects
          .Join(
              discoveredTests,
              proj => proj.DllPath,
              test => test.Key.Replace("\\", "/"),
              (proj, test) => new { proj.OutFile, Tests = test.Value }
          )
          .ToList().ForEach(x => outFileWriter.WriteDiscoveredTests(x.Tests, x.OutFile));
  }

  public void RunTests(
    string dllPath,
    Guid[] testIds,
    string outFile
)
  {
    var vsTestPath = GetVsTestPath();
    logService.Info($"Using VSTest path: {vsTestPath}");
    var testResults = RunHandler.RunTests(vsTestPath, dllPath, testIds);
    outFileWriter.WriteTestRunResults(testResults, outFile);
  }

  private string GetVsTestPath()
  {
    var x = MsBuildService.QuerySdkInstallations();
    return Path.Join(x.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

}

public sealed record DiscoverProjectRequest(string DllPath, string OutFile);