using System.Collections.Concurrent;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using Microsoft.Extensions.Logging;
using Porta.Pty;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Editor;

public sealed class PtyTerminalService(
  IEditorProcessManagerService editorProcessManagerService,
  JsonRpc jsonRpc,
  ILogger<PtyTerminalService> logger) : IPtyTerminalService
{
  private const int ReadBufferSize = 8192;

  private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(5);

  private sealed record Session(IPtyConnection Pty, Lock WriteLock);

  private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

  public async Task<int> StartAsync(Guid jobId, RunCommand command, int rows, int cols, CancellationToken ct = default)
  {
    var options = new PtyOptions
    {
      App = command.Executable,
      CommandLine = [.. command.Arguments],
      Cwd = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? Directory.GetCurrentDirectory() : command.WorkingDirectory,
      Rows = rows > 0 ? rows : 24,
      Cols = cols > 0 ? cols : 80,
      Environment = BuildEnvironment(command.EnvironmentVariables),
    };

    var pty = await PtyProvider.SpawnAsync(options, ct);
    var session = new Session(pty, new Lock());
    _sessions[jobId] = session;

    var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    pty.ProcessExited += (_, args) => exited.TrySetResult(args.ExitCode);

    _ = RunToCompletionAsync(jobId, session, exited.Task);

    return pty.Pid;
  }

  public void Write(Guid jobId, byte[] data)
  {
    if (!_sessions.TryGetValue(jobId, out var session))
      return;

    try
    {
      lock (session.WriteLock)
      {
        session.Pty.WriterStream.Write(data, 0, data.Length);
        session.Pty.WriterStream.Flush();
      }
    }
    catch (Exception e) when (e is IOException or ObjectDisposedException)
    {
      logger.LogDebug(e, "Dropped input for terminal job {JobId}: the pty is closed", jobId);
    }
  }

  public void Resize(Guid jobId, int cols, int rows)
  {
    if (!_sessions.TryGetValue(jobId, out var session))
      return;

    try
    {
      session.Pty.Resize(cols, rows);
    }
    catch (Exception e)
    {
      logger.LogDebug(e, "Failed to resize terminal job {JobId}", jobId);
    }
  }

  public void Kill(Guid jobId)
  {
    if (!_sessions.TryGetValue(jobId, out var session))
      return;

    try
    {
      session.Pty.Kill();
    }
    catch (Exception e)
    {
      logger.LogDebug(e, "Failed to kill terminal job {JobId}", jobId);
    }

  }

  private async Task RunToCompletionAsync(Guid jobId, Session session, Task<int> exited)
  {
    var exitCode = -1;

    try
    {
      await PumpOutputAsync(jobId, session.Pty);
      exitCode = await WaitForExitCodeAsync(jobId, session.Pty, exited);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Terminal job {JobId} failed", jobId);
    }
    finally
    {
      _sessions.TryRemove(jobId, out _);
      try
      {
        session.Pty.Dispose();
      }
      catch (Exception e)
      {
        logger.LogDebug(e, "Failed to dispose pty for terminal job {JobId}", jobId);
      }
    }

    try
    {
      await jsonRpc.NotifyWithParameterObjectAsync("terminal/exit", new TerminalExitNotification(jobId, exitCode));
    }
    catch (Exception e)
    {
      logger.LogDebug(e, "Failed to notify exit for terminal job {JobId}", jobId);
    }

    editorProcessManagerService.CompleteJob(jobId, exitCode);
  }

  private async Task<int> WaitForExitCodeAsync(Guid jobId, IPtyConnection pty, Task<int> exited)
  {
    if (exited.IsCompleted)
      return await exited;

    var grace = (int)ExitGracePeriod.TotalMilliseconds;
    if (await Task.Run(() => pty.WaitForExit(grace)))
      return pty.ExitCode;

    if (exited.IsCompleted)
      return await exited;

    logger.LogWarning("Terminal job {JobId} reached EOF but never reported an exit code", jobId);
    return -1;
  }

  private async Task PumpOutputAsync(Guid jobId, IPtyConnection pty)
  {
    var buffer = new byte[ReadBufferSize];

    while (true)
    {
      int read;
      try
      {
        read = await pty.ReaderStream.ReadAsync(buffer);
      }
      catch (IOException)
      {
        return;
      }
      catch (ObjectDisposedException)
      {
        return;
      }

      if (read <= 0)
        return;

      await jsonRpc.NotifyWithParameterObjectAsync(
          "terminal/output",
          new TerminalOutputNotification(jobId, buffer[..read]));
    }
  }

  private static Dictionary<string, string> BuildEnvironment(Dictionary<string, string> overrides)
  {
    var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
    {
      if (entry.Key is string key && entry.Value is string value)
        env[key] = value;
    }

    foreach (var (key, value) in overrides)
      env[key] = value;

    if (!env.ContainsKey("TERM"))
      env["TERM"] = "xterm-256color";

    return env;
  }
}