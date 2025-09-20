using System;
using EasyDotnet.Controllers.Initialize;

namespace EasyDotnet.Services;

public class ClientService
{
  public bool IsInitialized { get; set; }
  public bool UseVisualStudio { get; set; } = false;
  public ProjectInfo? ProjectInfo { get; set; }
  public ClientInfo? ClientInfo { get; set; }

  public void ThrowIfNotInitialized()
  {
    if (!IsInitialized)
    {
      throw new Exception("Client has not initialized yet");
    }
  }
}