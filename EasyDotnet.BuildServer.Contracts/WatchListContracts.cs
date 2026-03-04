namespace EasyDotnet.BuildServer.Contracts;

public sealed record GetWatchListRequest(
    string ProjectPath,
    string Configuration
);

public sealed record GetWatchListResponse(Dictionary<string, WatchListForProject> Projects);

public sealed record WatchListForProject(
  string[] Files,
  string[] StaticFiles
);