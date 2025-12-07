using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace EasyDotnet.IDE.OutputWindow;

public sealed class PipeConnection
{
  private NamedPipeClientStream? _pipeClient;

  public async Task<NamedPipeClientStream> ConnectAsync(
    string pipeName,
    CancellationToken cancellationToken = default)
  {
    _pipeClient = new NamedPipeClientStream(
      serverName: ".",
      pipeName: pipeName,
      direction: PipeDirection.InOut,
      options: PipeOptions.Asynchronous);

    try
    {
      await _pipeClient.ConnectAsync(5000, cancellationToken);
    }
    catch (TimeoutException)
    {
      throw new TimeoutException($"Failed to connect to named pipe '{pipeName}' within 5 seconds");
    }
    return _pipeClient;
  }

}