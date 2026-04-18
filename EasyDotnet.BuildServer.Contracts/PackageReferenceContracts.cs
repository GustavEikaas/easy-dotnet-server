namespace EasyDotnet.BuildServer.Contracts;

public sealed record ListPackageReferencesRequest(string ProjectPath);

public sealed record InstalledPackageReference(string Id, string Version);
