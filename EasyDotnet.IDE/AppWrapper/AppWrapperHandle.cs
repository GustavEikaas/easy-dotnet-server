using EasyDotnet.AppWrapper.Contracts;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;

namespace EasyDotnet.IDE.AppWrapper;

public class AppWrapperHandle(AppWrapperEntry entry) : IAppWrapperHandle
{
  public async Task<int> SendRunCommandAsync(Guid jobId, RunCommand command, CancellationToken ct)
  {
    var runAppCommand = new RunAppCommand(
        jobId,
        command.Executable,
        [.. command.Arguments],
        command.WorkingDirectory,
        command.EnvironmentVariables);

    try
    {
      var pid = await entry.Rpc.InvokeWithParameterObjectAsync<int>("appWrapper/run", runAppCommand, ct);
      entry.SetJob(jobId);
      return pid;
    }
    catch
    {
      entry.SetIdle();
      throw;
    }
  }

  public async Task TerminateAsync()
  {
    try
    {
      await entry.Rpc.NotifyWithParameterObjectAsync("appWrapper/terminate");
    }
    catch { }
  }
}