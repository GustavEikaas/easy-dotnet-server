using static EasyDotnet.Services.OutdatedService;

namespace EasyDotnet.Controllers.Outdated;

public sealed record OutdatedDependencyInfoResponse(
      string Name,
      string CurrentVersion,
      string LatestVersion,
      string TargetFramework,
      bool IsOutdated,
      bool IsTransitive,
      string UpgradeSeverity
  );

public static class OutdatedDependencyExtensions
{
  public static OutdatedDependencyInfoResponse ToResponse(this DependencyInfo props)
      => new(
          Name: props.Name,
          CurrentVersion: props.CurrentVersion,
          LatestVersion: props.LatestVersion,
          TargetFramework: props.TargetFramework,
          IsOutdated: props.IsOutdated,
          IsTransitive: props.IsTransitive,
          UpgradeSeverity: props.UpgradeSeverity
      );
}