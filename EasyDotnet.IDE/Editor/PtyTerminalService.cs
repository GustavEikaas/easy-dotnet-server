using System.Collections.Concurrent;
using System.Globalization;
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

  private static readonly TimeSpan StartupProbeWindow = TimeSpan.FromMilliseconds(250);

  private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);

  private sealed class Session(IPtyConnection pty)
  {
    public IPtyConnection Pty { get; } = pty;

    public Lock WriteLock { get; } = new();

    private int _completed;

    public bool TryClaimCompletion() => Interlocked.Exchange(ref _completed, 1) == 0;
  }

  private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

  public async Task<int> StartAsync(Guid jobId, RunCommand command, int rows, int cols, CancellationToken ct = default)
  {
    var effectiveRows = rows > 0 ? rows : 24;
    var effectiveCols = cols > 0 ? cols : 80;

    var options = new PtyOptions
    {
      App = command.Executable,
      CommandLine = [.. command.Arguments],
      Cwd = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? Directory.GetCurrentDirectory() : command.WorkingDirectory,
      Rows = effectiveRows,
      Cols = effectiveCols,
      Environment = BuildEnvironment(command.EnvironmentVariables, effectiveRows, effectiveCols),
      UseAsyncIo = true,
    };

    var pty = await PtyProvider.SpawnAsync(options, ct);
    var session = new Session(pty);
    _sessions[jobId] = session;

    var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    pty.ProcessExited += (_, args) => exited.TrySetResult(args.ExitCode);

    var producedOutput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pump = Task.Run(() => PumpOutputAsync(jobId, pty, producedOutput), CancellationToken.None);

    await Task.WhenAny(producedOutput.Task, pump, exited.Task, Task.Delay(StartupProbeWindow, ct));

    if (!producedOutput.Task.IsCompleted && (pump.IsCompleted || exited.Task.IsCompleted))
    {
      var exitCode = await WaitForExitCodeAsync(jobId, pty, exited.Task);
      if (exitCode != 0)
      {
        await AbandonAsync(jobId, session, pump);
        throw new InvalidOperationException(
            $"'{command.Executable}' produced no output and exited with code {exitCode}. " +
            $"Check that the executable exists and that the working directory '{options.Cwd}' is valid.");
      }
    }

    _ = RunToCompletionAsync(jobId, session, pump, exited.Task);

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

    _ = ForceCompleteAfterKillAsync(jobId, session);
  }

  private async Task ForceCompleteAfterKillAsync(Guid jobId, Session session)
  {
    await Task.Delay(KillTimeout);

    if (!_sessions.TryGetValue(jobId, out var current) || !ReferenceEquals(current, session))
      return;

    logger.LogWarning("Terminal job {JobId} did not exit within {Seconds}s of being killed; completing it anyway", jobId, KillTimeout.TotalSeconds);

    _sessions.TryRemove(jobId, out _);
    DisposePty(jobId, session);

    await CompleteAsync(jobId, session, -1);
  }

  private async Task AbandonAsync(Guid jobId, Session session, Task pump)
  {
    session.TryClaimCompletion();
    _sessions.TryRemove(jobId, out _);

    await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(1)));

    DisposePty(jobId, session);
  }

  private async Task RunToCompletionAsync(Guid jobId, Session session, Task pump, Task<int> exited)
  {
    var exitCode = -1;

    try
    {
      await pump;
      exitCode = await WaitForExitCodeAsync(jobId, session.Pty, exited);
    }
    catch (Exception e)
    {
      logger.LogError(e, "Terminal job {JobId} failed", jobId);
    }
    finally
    {
      _sessions.TryRemove(jobId, out _);
      DisposePty(jobId, session);
    }

    await CompleteAsync(jobId, session, exitCode);
  }

  private async Task CompleteAsync(Guid jobId, Session session, int exitCode)
  {
    if (!session.TryClaimCompletion())
      return;

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

  private void DisposePty(Guid jobId, Session session)
  {
    try
    {
      session.Pty.Dispose();
    }
    catch (Exception e)
    {
      logger.LogDebug(e, "Failed to dispose pty for terminal job {JobId}", jobId);
    }
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

  private async Task PumpOutputAsync(Guid jobId, IPtyConnection pty, TaskCompletionSource producedOutput)
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

      producedOutput.TrySetResult();

      await jsonRpc.NotifyWithParameterObjectAsync(
          "terminal/output",
          new TerminalOutputNotification(jobId, buffer[..read]));
    }
  }

  private static Dictionary<string, string> BuildEnvironment(Dictionary<string, string> overrides, int rows, int cols)
  {
    var env = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
    {
      if (entry.Key is string key && entry.Value is string value)
        env[key] = value;
    }

    env["TERM"] = "xterm-256color";
    env["LINES"] = rows.ToString(CultureInfo.InvariantCulture);
    env["COLUMNS"] = cols.ToString(CultureInfo.InvariantCulture);

    foreach (var (key, value) in overrides)
      env[key] = value;

    return env;
  }
}