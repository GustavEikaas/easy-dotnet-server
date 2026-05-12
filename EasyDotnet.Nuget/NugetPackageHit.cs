namespace EasyDotnet.Nuget;

public sealed record NugetPackageHit(
    string Source,
    string Id,
    string Version,
    string? Description,
    long? DownloadCount,
    string? Authors);