using EasyDotnet.IDE.Models.LaunchProfile;

namespace EasyDotnet.IDE.Interfaces;

public interface ILaunchProfileService
{
  LaunchProfile? GetLaunchProfile(string targetPath, string? profileName);
  Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath);
}