using EasyDotnet.Application.Interfaces;
using EasyDotnet.Domain.Models.Client;
using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Services;

public sealed record SetBreakpointRequest(string Path, int LineNumber);
public sealed record OpenBufferRequest(string Path);

public class ClientService(JsonRpc rpc) : IClientService
{
  public bool IsInitialized { get; set; }
  public bool UseVisualStudio { get; set; } = false;
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

  public async Task<bool> RequestOpenBuffer(string path) => await rpc.InvokeWithParameterObjectAsync<bool>("openBuffer", new OpenBufferRequest(path));
  public async Task<bool> RequestSetBreakpoint(string path, int lineNumber) => await rpc.InvokeWithParameterObjectAsync<bool>("setBreakpoint", new SetBreakpointRequest(path, lineNumber));
}