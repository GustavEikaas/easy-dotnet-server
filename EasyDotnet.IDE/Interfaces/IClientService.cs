using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.IDE.Interfaces;

public interface IClientService
{
  bool IsInitialized { get; set; }
  bool UseVisualStudio { get; set; }
  bool HasExternalTerminal { get; }
  bool SupportsSingleFileExecution { get; set; }
  ProjectInfo? ProjectInfo { get; set; }
  ClientInfo? ClientInfo { get; set; }
  ClientOptions? ClientOptions { get; set; }

  void ThrowIfNotInitialized();
  string RequireSolutionFile();
  string RequireRootDir();
}