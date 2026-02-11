namespace EasyDotnet.BuildServer.Contracts;

public sealed record SdkInstallation(string Name, string Moniker, Version Version, string MSBuildPath, string VisualStudioRootPath);
