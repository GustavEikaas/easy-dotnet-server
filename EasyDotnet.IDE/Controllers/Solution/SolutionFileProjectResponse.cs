namespace EasyDotnet.Controllers.Solution;

public sealed record SolutionFileProjectResponse(
    string ProjectName,
    string AbsolutePath
);