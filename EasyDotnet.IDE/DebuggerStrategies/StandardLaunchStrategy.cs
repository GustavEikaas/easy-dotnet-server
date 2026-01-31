using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.Domain.Models.LaunchProfile;
using EasyDotnet.IDE.Types;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class StandardLaunchStrategy(
    string? launchProfileName,
    ILaunchProfileService launchProfileService
    ) : IDebugSessionStrategy
{
  private LaunchProfile? _activeProfile;
  private DotnetProject? _project;

  public Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _project = project;

    if (!string.IsNullOrEmpty(launchProfileName) &&
        launchProfileService.GetLaunchProfiles(project.TargetPath!) is { } profiles &&
        profiles.TryGetValue(launchProfileName, out var profile))
    {
      _activeProfile = profile;
    }

    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request)
  {
    if (_project == null) throw new InvalidOperationException("Strategy has not been prepared.");

    var env = DebugStrategyUtils.GetEnvironmentVariables(_activeProfile);

    var cwd = !string.IsNullOrWhiteSpace(_activeProfile?.WorkingDirectory)
        ? DebugStrategyUtils.NormalizePath(DebugStrategyUtils.InterpolateVariables(_activeProfile.WorkingDirectory, _project))
        : _project.ProjectDir;

    request.Type = "request";
    request.Command = "launch";
    request.Arguments.Request = "launch";
    request.Arguments.Program = _project.TargetPath;
    request.Arguments.Cwd = cwd;

    if (!string.IsNullOrWhiteSpace(_activeProfile?.CommandLineArgs))
    {
      var interpolatedArgs = DebugStrategyUtils.InterpolateVariables(_activeProfile.CommandLineArgs, _project);
      request.Arguments.Args = DebugStrategyUtils.SplitCommandLineArgs(interpolatedArgs);
    }

    request.Arguments.Env = (request.Arguments.Env ?? [])
        .Concat(env)
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return Task.CompletedTask;
  }

  public Task<int>? GetProcessIdAsync() => null!;

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}