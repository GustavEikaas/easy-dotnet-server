using EasyDotnet.Domain.Models.LaunchProfile;

namespace EasyDotnet.Application.Interfaces;

public interface ILaunchProfileService
{
  Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath);
}
