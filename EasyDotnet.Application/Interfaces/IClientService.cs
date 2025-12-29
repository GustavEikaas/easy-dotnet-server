using EasyDotnet.Domain.Models.Client;

namespace EasyDotnet.Application.Interfaces;

public interface IClientService
{
  bool IsInitialized { get; set; }
  bool UseVisualStudio { get; set; }
  ProjectInfo? ProjectInfo { get; set; }
  ClientInfo? ClientInfo { get; set; }
  ClientOptions? ClientOptions { get; set; }

  void ThrowIfNotInitialized();
}