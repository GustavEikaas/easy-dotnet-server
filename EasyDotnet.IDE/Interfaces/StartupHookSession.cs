using System.IO.Pipes;

namespace EasyDotnet.IDE.Interfaces;

public sealed class StartupHookSession(
    NamedPipeServerStream pipe,
    Dictionary<string, string> environmentVariables) : IAsyncDisposable
{
  public Dictionary<string, string> EnvironmentVariables { get; } = environmentVariables;

  public async Task WaitForConnectionAsync(CancellationToken ct = default)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    try
    {
      await pipe.WaitForConnectionAsync(timeout.Token);
    }
    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException(BuildTimeoutDiagnostic(), ex);
    }
  }

  private string BuildTimeoutDiagnostic()
  {
    var hooks = EnvironmentVariables.TryGetValue("DOTNET_STARTUP_HOOKS", out var h) ? h : "<unset>";
    var pipeName = EnvironmentVariables.TryGetValue("EASY_DOTNET_HOOK_PIPE", out var p) ? p : "<unset>";
    return
        $"Timed out after 5s waiting for StartupHook pipe connection (hook never attached). " +
        $"This usually means DOTNET_STARTUP_HOOKS was not inherited by the spawned process, or the hook crashed before connecting. " +
        $"To diagnose, add these env vars to your launchSettings.json profile and re-run: " +
        $"EASY_DOTNET_HOOK_DEBUG=1, EASY_DOTNET_HOOK_LOG_FILE=/tmp/easy-dotnet-hook.log. " +
        $"Expected DOTNET_STARTUP_HOOKS='{hooks}', EASY_DOTNET_HOOK_PIPE='{pipeName}'.";
  }

  public void Resume()
  {
    if (!pipe.IsConnected)
      throw new InvalidOperationException("StartupHook is not connected, cannot resume");
    pipe.WriteByte(1);
    pipe.Flush();
  }

  public async ValueTask DisposeAsync() => await pipe.DisposeAsync();
}