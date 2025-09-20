namespace EasyDotnet.Domain.Models.Solution;

public sealed record SolutionFileProject(
    string ProjectName,
    string AbsolutePath
);