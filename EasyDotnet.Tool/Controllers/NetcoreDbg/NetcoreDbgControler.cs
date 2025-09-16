using EasyDotnet.Services.NetCoreDbg;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.NetcoreDbg;

public class NetcoreDbgControler(NetcoreDbgService netcoreDbgService) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public void StartDebugger() => netcoreDbgService.Start();
}