namespace EasyDotnet.Application.Interfaces;

public interface IVisualStudioLocator
{
  string? GetApplicationHostConfig();
  string? GetIisExpressExe();
  Task<string> GetVisualStudioMSBuildPath();
}