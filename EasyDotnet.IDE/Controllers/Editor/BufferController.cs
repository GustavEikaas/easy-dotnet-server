using EasyDotnet.Controllers;
using EasyDotnet.IDE.Interfaces;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Editor;

public sealed record BufferEventRequest(string Path);

public class BufferController(IOpenBufferService buffers) : BaseController
{
  [JsonRpcMethod("buffer/opened", UseSingleObjectParameterDeserialization = true)]
  public void Opened(BufferEventRequest request) => buffers.OnBufferOpened(request.Path);

  [JsonRpcMethod("buffer/closed", UseSingleObjectParameterDeserialization = true)]
  public void Closed(BufferEventRequest request) => buffers.OnBufferClosed(request.Path);
}
