namespace EasyDotnet.IDE.Interfaces;

public interface IVisualStudioLocator
{
  string? GetApplicationHostConfig();
  string? GetIisExpressExe();
  Task<string> GetVisualStudioMSBuildPath();
}
