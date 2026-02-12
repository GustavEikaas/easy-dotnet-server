namespace EasyDotnet.BuildServer.Contracts;

public enum MsBuildInstanceOrigin
{
  SDK = 0,
  VisualStudio = 1
}

public sealed record MsBuildInstance(
  string Name,
  string Moniker,
  Version Version,
  string MSBuildPath,
  string VisualStudioRootPath,
  MsBuildInstanceOrigin MsBuildInstanceOrigin
);