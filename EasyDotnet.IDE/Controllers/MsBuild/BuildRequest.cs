namespace EasyDotnet.Controllers.MsBuild;

public sealed record BuildRequest(
  string TargetPath,
  string? TargetFramework,
  string? BuildArgs,
  string? Configuration
)
{
  public string ConfigurationOrDefault => Configuration ?? "Debug";
}