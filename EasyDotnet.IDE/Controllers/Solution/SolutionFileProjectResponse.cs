namespace EasyDotnet.IDE.Controllers.Solution;

public sealed record SolutionFileProjectResponse(
    string ProjectName,
    string AbsolutePath
);