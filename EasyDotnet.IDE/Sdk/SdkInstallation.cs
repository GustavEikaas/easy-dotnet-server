namespace EasyDotnet.IDE.Sdk;

public sealed record SdkInstallation(string Name, string Moniker, Version Version, string MSBuildPath, string VisualStudioRootPath);
