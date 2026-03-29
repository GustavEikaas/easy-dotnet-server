using EasyDotnet.BuildServer.Contracts;
using LaunchProfile = EasyDotnet.Domain.Models.LaunchProfile.LaunchProfile;

namespace EasyDotnet.IDE.Workspace.Services;

public enum ExecutionTargetKind { Project, SingleFile }

public record ResolvedExecutionTarget
{
  public required ExecutionTargetKind Kind { get; init; }

  public ValidatedDotnetProject? Project { get; init; }

  public string? SingleFilePath { get; init; }

  public string? LaunchProfileName { get; init; }

  public LaunchProfile? LaunchProfile { get; init; }
}