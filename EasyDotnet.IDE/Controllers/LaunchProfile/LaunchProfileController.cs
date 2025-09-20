using System.Collections.Generic;
using System.Linq;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.LaunchProfile;

public class LaunchProfileController(ILaunchProfileService launchProfileService) : BaseController
{
  [JsonRpcMethod("launch-profiles")]
  public IAsyncEnumerable<LaunchProfileResponse> GetLaunchProfiles(string targetPath)
  {
    var profiles = launchProfileService.GetLaunchProfiles(targetPath);

    return (profiles?.Select(x => new LaunchProfileResponse(x.Key, x.Value)) ?? []).AsAsyncEnumerable();
  }
}