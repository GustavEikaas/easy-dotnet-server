namespace EasyDotnet.IDE.Solution.Controllers;

public sealed record SolutionSetBuildConfigurationRequest(string? BuildType, string? Platform);