namespace EasyDotnet.IDE.Controllers.MsBuild;

public sealed record CleanRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration
)
{
  public string ConfigurationOrDefault => Configuration ?? "Debug";
}