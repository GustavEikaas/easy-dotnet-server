using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Types;
using EasyDotnet.VSTest;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.Services;

public class VsTestService(IMsBuildService msBuildService, ILogger<VsTestService> logService)
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

  private string GetVsTestPath()
  {
    var sdk = msBuildService.QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

}