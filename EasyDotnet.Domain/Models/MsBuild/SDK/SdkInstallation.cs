namespace EasyDotnet.Domain.Models.MsBuild.SDK;

public sealed record SdkInstallation(string Name, string Moniker, Version Version, string MSBuildPath, string VisualStudioRootPath);