using EasyDotnet.Debugger;
using EasyDotnet.Debugger.Messages;
using EasyDotnet.IDE.Types;
using EasyDotnet.MsBuild;

namespace EasyDotnet.IDE.DebuggerStrategies;

public class StandardLaunchStrategy() : IDebugSessionStrategy
{
  private DotnetProject? _project;

  public Task PrepareAsync(DotnetProject project, CancellationToken ct)
  {
    _project = project;
    return Task.CompletedTask;
  }

  public Task TransformRequestAsync(InterceptableAttachRequest request, IDebuggerProxy proxy)
  {
    if (_project == null) throw new InvalidOperationException("Strategy has not been prepared.");

    var cwd = _project.ProjectDir;

    request.Type = "request";
    request.Command = "launch";
    request.Arguments.Request = "launch";
    request.Arguments.Program = _project.TargetPath;
    request.Arguments.Cwd = cwd;

    return Task.CompletedTask;
  }

  public void OnDebugSessionReady(DebugSession debugSession, IDebuggerProxy proxy) { }

  public Task<int>? GetProcessIdAsync() => null!;

  public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}