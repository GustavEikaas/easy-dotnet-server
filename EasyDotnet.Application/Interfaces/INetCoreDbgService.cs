using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;

namespace EasyDotnet.Application.Interfaces;

public interface INetcoreDbgService
{
  Task Completion { get; }

  ValueTask DisposeAsync();
  void Start(DotnetProject project, string projectPath, LaunchProfile? launchProfile);
}
