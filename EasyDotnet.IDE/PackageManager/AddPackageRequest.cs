namespace EasyDotnet.IDE.PackageManager;

public sealed record AddPackageRequest(
    string? ProjectPath = null,
    bool IncludePrerelease = false);