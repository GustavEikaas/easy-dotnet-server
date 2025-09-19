using System.Collections.Generic;
using System.Linq;
using EasyDotnet.Services;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.LaunchProfile;

public sealed record LaunchProfileResponse(string Name, LaunchProfile Value);

public class LaunchProfileController(LaunchProfileService launchProfileService) : BaseController
{
  [JsonRpcMethod("launch-profiles")]
  public IAsyncEnumerable<LaunchProfileResponse> GetLaunchProfiles(string targetPath)
  {
    var profiles = launchProfileService.GetLaunchProfiles(targetPath);

    return (profiles.Select(x => new LaunchProfileResponse(x.Key, x.Value)) ?? []).AsAsyncEnumerable();
  }
}