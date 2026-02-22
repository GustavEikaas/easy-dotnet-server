using EasyDotnet.Domain.Models.LaunchProfile;

namespace EasyDotnet.Application.Interfaces;

public interface ILaunchProfileService
{
  LaunchProfile? GetLaunchProfile(string targetPath, string? profileName);
  Dictionary<string, LaunchProfile>? GetLaunchProfiles(string targetPath);
}