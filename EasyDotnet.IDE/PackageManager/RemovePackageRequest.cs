namespace EasyDotnet.IDE.PackageManager;

public sealed record RemovePackageRequest(string? ProjectPath = null, string[]? PackageIds = null);
