namespace EasyDotnet.Controllers.MsBuild;

public sealed record ProjectPropertiesRequest(
  string TargetPath,
  string? TargetFramework,
  string? OutFile,
  string? Configuration
)
{
  public string ConfigurationOrDefault => Configuration ?? "Debug";
}