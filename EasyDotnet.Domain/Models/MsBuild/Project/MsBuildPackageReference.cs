namespace EasyDotnet.Domain.Models.MsBuild.Project;

public sealed record DotnetListPackageOutput(
    int Version,
    string Parameters,
    List<ProjectInfo> Projects
);

public sealed record ProjectInfo(
    string Path,
    List<FrameworkInfo> Frameworks
);

public sealed record FrameworkInfo(
    string Framework,
    List<PackageReference> TopLevelPackages
);

public sealed record PackageReference(
    string Id,
    string RequestedVersion,
    string ResolvedVersion
);