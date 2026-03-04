using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Infrastructure.Services;

public class ClientService : IClientService
{
  public bool IsInitialized { get; set; }
  public bool UseVisualStudio { get; set; }
  public bool HasExternalTerminal => !string.IsNullOrEmpty(ClientOptions?.ExternalTerminal?.Command);
  public ProjectInfo? ProjectInfo { get; set; }
  public ClientInfo? ClientInfo { get; set; }
  public ClientOptions? ClientOptions { get; set; }

  public void ThrowIfNotInitialized()
  {
    if (!IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
  }

  public string RequireSolutionFile() => ProjectInfo?.SolutionFile ?? throw new InvalidOperationException("Solution file is required for this feature");
  public string RequireRootDir() => ProjectInfo?.RootDir ?? throw new InvalidOperationException("Root directory is required for this feature");
}