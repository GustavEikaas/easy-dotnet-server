using NuGet.Configuration;

namespace EasyDotnet.Controllers.Nuget;

public sealed record NugetSourceResponse(string Name, string Uri, bool IsLocal);

public static class NugetSourceExtensions
{
  public static NugetSourceResponse ToResponse(this PackageSource props)
      => new(
          Name: props.Name,
          Uri: props.Source,
          IsLocal: props.IsLocal
      );
}