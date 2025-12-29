using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Services;

public class ClientService : IClientService
{
  public bool IsInitialized { get; set; }
  public bool UseVisualStudio { get; set; }
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

}
