using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.BuildServer.SmokeTests;

internal sealed class BuildServerProcess : IAsyncDisposable
{
  public JsonRpc Rpc { get; }
  public Process Process { get; }
  public StringBuilder Stderr { get; }

  private BuildServerProcess(Process process, JsonRpc rpc, StringBuilder stderr)
  {
    Process = process;
    Rpc = rpc;
    Stderr = stderr;
  }

  public static async Task<BuildServerProcess> StartAsync(
      string fileName,
      string[] leadingArgs,
      TimeSpan connectTimeout)
  {
    var pipeName = "ed-smoke-" + Guid.NewGuid().ToString("N");

    var psi = new ProcessStartInfo
    {
      FileName = fileName,
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    foreach (var a in leadingArgs) psi.ArgumentList.Add(a);
    psi.ArgumentList.Add("--pipe");
    psi.ArgumentList.Add(pipeName);
    psi.ArgumentList.Add("--log-level=information");

    var stderr = new StringBuilder();
    var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
    proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };
    proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

    if (!proc.Start())
    {
      throw new InvalidOperationException($"Failed to start {fileName}");
    }
    proc.BeginErrorReadLine();
    proc.BeginOutputReadLine();

    var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    try
    {
      using var cts = new CancellationTokenSource(connectTimeout);
      await client.ConnectAsync(cts.Token);
    }
    catch (Exception ex)
    {
      try { if (!proc.HasExited) proc.Kill(true); } catch { }
      proc.WaitForExit(2000);
      var captured = stderr.ToString();
      throw new InvalidOperationException(
          $"BuildServer ({fileName}) did not accept pipe connection within {connectTimeout.TotalSeconds}s. " +
          $"Exited={proc.HasExited} ExitCode={(proc.HasExited ? proc.ExitCode : -1)}\n--- stderr/stdout ---\n{captured}",
          ex);
    }

    var formatter = new JsonMessageFormatter
    {
      JsonSerializer =
      {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
      },
    };
    var handler = new HeaderDelimitedMessageHandler(client, client, formatter);
    var rpc = new JsonRpc(handler);
    rpc.StartListening();
    return new BuildServerProcess(proc, rpc, stderr);
  }

  public async ValueTask DisposeAsync()
  {
    try { Rpc.Dispose(); } catch { }
    try
    {
      if (!Process.HasExited) Process.Kill(true);
    }
    catch { }
    try { await Task.Run(() => Process.WaitForExit(2000)); } catch { }
    Process.Dispose();
  }
}