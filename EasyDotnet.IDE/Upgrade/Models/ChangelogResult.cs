namespace EasyDotnet.IDE.Upgrade.Models;

// Source values: "github" | "nuspec" | "none"
public sealed record ChangelogResult(
    string PackageId,
    string Version,
    string? Body,
    string Source,
    string? GitHubReleaseUrl,
    string? ProjectUrl,
    string NugetUrl
);