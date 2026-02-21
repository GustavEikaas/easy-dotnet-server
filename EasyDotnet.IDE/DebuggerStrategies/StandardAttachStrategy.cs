using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using EasyDotnet.MsBuild;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class StandardAttachStrategy(ILogger<StandardAttachStrategy> logger, int pid) : IDebugSessionStrategy
{
  private DotnetProject? _project;
  private readonly TaskCompletionSource<int> _processIdTcs = new();

  public Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _project = project;


    logger.LogInformation("Starting Pid attach for {Project} with PID {pid}", project.ProjectName, pid);

    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request)
  {

    request.Type = "request";
    request.Command = "attach";
    request.Arguments.Request = "attach";
    request.Arguments.ProcessId = pid;
    if (_project?.ProjectDir is not null)
    {
      request.Arguments.Cwd = _project.ProjectDir;
    }

    return Task.CompletedTask;
  }

  public Task<int>? GetProcessIdAsync() => _processIdTcs.Task;

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  public void OnDebugSessionReady(DebugSession debugSession) => throw new NotImplementedException();

}