using EasyDotnet.IDE.Models.MsBuild.SDK;
using Microsoft.Build.Locator;

namespace EasyDotnet.IDE.Sdk;

public class SdkService
{
  public SdkInstallation[] QuerySdkInstallations()
  {
    MSBuildLocator.AllowQueryAllRuntimeVersions = true;
    var instances = MSBuildLocator.QueryVisualStudioInstances()
        .Where(x => x.DiscoveryType == DiscoveryType.DotNetSdk)
        .ToList();

    return [.. instances.Select(x => new SdkInstallation(
        x.Name,
        $"net{x.Version.Major}.0",
        x.Version,
        x.MSBuildPath,
        x.VisualStudioRootPath))];
  }

  public string GetVsTestPath()
  {
    var sdk = QuerySdkInstallations();
    return Path.Join(sdk.ToList()[0].MSBuildPath, "vstest.console.dll");
  }

  public string GetDotnetSdkBasePath() =>
      Path.GetDirectoryName(Path.GetDirectoryName(QuerySdkInstallations().First().MSBuildPath))!;
}
