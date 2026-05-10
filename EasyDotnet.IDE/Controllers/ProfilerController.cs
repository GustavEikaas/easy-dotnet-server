using EasyDotnet.Controllers;
using EasyDotnet.IDE.Services;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers;

public class ProfilerController(ProfilerService profilerService) : BaseController
{
  [JsonRpcMethod("profiler/start", UseSingleObjectParameterDeserialization = true)]
  public Task<ProfilerStartResponse> Start(ProfilerStartRequest request) => profilerService.StartAsync(request.Pid, request.DurationSeconds);

  [JsonRpcMethod("profiler/stop")]
  public Task Stop() => profilerService.StopAsync();
}
