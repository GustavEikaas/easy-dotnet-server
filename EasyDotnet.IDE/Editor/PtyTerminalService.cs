using System.Collections.Concurrent;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using Porta.Pty;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Editor;

public sealed class PtyTerminalService(
  IEditorProcessManagerService editorProcessManagerService,
  JsonRpc jsonRpc) : IPtyTerminalService
{
  private const int ReadBufferSize = 8192;

  private readonly ConcurrentDictionary<Guid, IPtyConnection> _sessions = new();

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
    _sessions[jobId] = pty;

    pty.ProcessExited += (_, args) =>
    {
      _sessions.TryRemove(jobId, out IPtyConnection? _);
      _ = jsonRpc.NotifyWithParameterObjectAsync("terminal/exit", new TerminalExitNotification(jobId, args.ExitCode));
      editorProcessManagerService.CompleteJob(jobId, args.ExitCode);
    };

    _ = PumpOutputAsync(jobId, pty);

    return pty.Pid;
  }

  public void Write(Guid jobId, byte[] data)
  {
    if (!_sessions.TryGetValue(jobId, out var pty))
      return;

    pty.WriterStream.Write(data, 0, data.Length);
    pty.WriterStream.Flush();
  }

  public void Resize(Guid jobId, int cols, int rows)
  {
    if (_sessions.TryGetValue(jobId, out var pty))
      pty.Resize(cols, rows);
  }

  public void Kill(Guid jobId)
  {
    if (_sessions.TryGetValue(jobId, out var pty))
      pty.Kill();
  }

  private async Task PumpOutputAsync(Guid jobId, IPtyConnection pty)
  {
    var buffer = new byte[ReadBufferSize];

    try
    {
      while (true)
      {
        var read = await pty.ReaderStream.ReadAsync(buffer);
        if (read <= 0)
          break;

        await jsonRpc.NotifyWithParameterObjectAsync(
            "terminal/output",
            new TerminalOutputNotification(jobId, buffer[..read]));
      }
    }
    catch (Exception)
    {
    }
  }

  private static Dictionary<string, string> BuildEnvironment(Dictionary<string, string> overrides)
  {
    var env = new Dictionary<string, string>();

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