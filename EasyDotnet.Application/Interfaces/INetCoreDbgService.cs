using System.Diagnostics;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.MsBuild;

namespace EasyDotnet.Application.Interfaces;

public interface INetcoreDbgService
{
  Task Completion { get; }

  ValueTask DisposeAsync();
  Task<int> Start(string binaryPath, DotnetProject project, string projectPath, LaunchProfile? launchProfile, (Process, int)? vsTestAttach);
}