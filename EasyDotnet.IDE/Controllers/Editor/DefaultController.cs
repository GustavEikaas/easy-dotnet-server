using EasyDotnet.Controllers;
using EasyDotnet.Infrastructure.Settings;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Editor;

// These endpoints will be used temporarily by client to sync local settings, until server migration is complete
public class DefaultController(SettingsService settingsService) : BaseController
{
  [JsonRpcMethod("set-default-startup-project")]
  public void SetDefaultRunProject(string projectPath) => settingsService.SetDefaultStartupProject(projectPath);

  [JsonRpcMethod("set-default-launch-profile")]
  public void SetDefaultLaunchProfileProject(string projectPath, string launchProfile) => settingsService.SetProjectLaunchProfile(projectPath, launchProfile);

  [JsonRpcMethod("set-default-test-project")]
  public void SetDefaultTestProject(string projectPath) => settingsService.SetDefaultTestProject(projectPath);

  [JsonRpcMethod("set-default-build-project")]
  public void SetDefaultBuildProject(string projectPath) => settingsService.SetDefaultBuildProject(projectPath);

  [JsonRpcMethod("set-default-view-project")]
  public void SetDefaultViewProject(string projectPath) => settingsService.SetDefaultViewProject(projectPath);
}