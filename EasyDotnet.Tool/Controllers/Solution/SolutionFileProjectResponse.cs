using Microsoft.Build.Construction;

namespace EasyDotnet.Controllers.Solution;

public sealed record SolutionFileProjectResponse(
    string ProjectName,
    string AbsolutePath
);

public static class SolutionFileProjectExtensions
{
  public static SolutionFileProjectResponse ToResponse(this ProjectInSolution props)
      => new(props.ProjectName, props.AbsolutePath);
}