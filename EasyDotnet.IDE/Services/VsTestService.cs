using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyDotnet.Types;
using EasyDotnet.VSTest;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services;

public class VsTestService(ILogger<VsTestService> logService)
{
  public List<DiscoveredTest> RunDiscover(string dllPath)
  {
    var vsTestPath = GetVsTestPath();
    logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
    var discoveredTests = DiscoverHandler.Discover(vsTestPath, [dllPath]);
    discoveredTests.TryGetValue(dllPath, out var tests);
    return tests ?? [];
  }

  public List<TestRunResult> RunTests(string dllPath, Guid[] testIds)
  {
    var vsTestPath = GetVsTestPath();
    logService.LogInformation("Using VSTest path: {vsTestPath}", vsTestPath);
    var testResults = RunHandler.RunTests(vsTestPath, dllPath, testIds);
    return testResults;
  }

  private static string GetVsTestPath()
  {
    var x = MsBuildService.QuerySdkInstallations();
    return Path.Join(x.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

}