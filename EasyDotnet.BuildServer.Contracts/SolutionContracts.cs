namespace EasyDotnet.BuildServer.Contracts;

public sealed record GetSolutionProjectsRequest(
    string SolutionPath
);

public sealed record GetSolutionProjectsResponse(
    IReadOnlyList<SolutionProjectItem> Projects
);

public sealed record SolutionProjectItem(
    string ProjectName,
    string AbsolutePath,
    string ProjectGuid
);