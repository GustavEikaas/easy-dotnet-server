using System.Diagnostics;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.Domain.Models.MsBuild.Project;

namespace EasyDotnet.Application.Interfaces;

public interface INetcoreDbgService
{
  Task Completion { get; }

  ValueTask DisposeAsync();
  void Start(string binaryPath, DotnetProject project, string projectPath, LaunchProfile? launchProfile, (Process, int)? vsTestAttach);
}