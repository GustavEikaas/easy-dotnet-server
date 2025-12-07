// EasyDotnet.IDE/Services/OutputWindowManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Debugger;
using EasyDotnet.IDE.Commands;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.OutputWindow;

public interface IOutputWindowManager
{
  Task<string?> StartOutputWindowAsync(string dllPath, CancellationToken cancellationToken);
  Task StopOutputWindowAsync(string dllPath);
  Task SendOutputAsync(string dllPath, DebugOutputEvent output);
  bool IsConnected(string dllPath);
}

public class OutputWindowConnection
{
  public required string PipeName { get; init; }
  public required Process Process { get; init; }
  public required NamedPipeServerStream PipeServer { get; init; }
  public required JsonRpc JsonRpc { get; init; }
  public required TaskCompletionSource<bool> Connected { get; init; }
  public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public class OutputWindowManager(ILogger<OutputWindowManager> logger) : IOutputWindowManager, IAsyncDisposable
{
  private readonly ConcurrentDictionary<string, OutputWindowConnection> _connections = new();
  private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

  // Update your OutputWindowManager.cs StartOutputWindowAsync method

  public async Task<string?> StartOutputWindowAsync(string dllPath, CancellationToken cancellationToken)
  {
    var projectName = Path.GetFileNameWithoutExtension(dllPath);

    if (_connections.ContainsKey(dllPath))
    {
      logger.LogWarning("Output window already exists for {project}", projectName);
      return null;
    }

    // Use PipeUtils to generate cross-platform compatible pipe name
    var pipeName = PipeUtils.GeneratePipeName();

    try
    {
      // Create named pipe server
      var pipeServer = new NamedPipeServerStream(
          pipeName,
          PipeDirection.InOut,
          1,
          PipeTransmissionMode.Byte,
          PipeOptions.Asynchronous);

      var connected = new TaskCompletionSource<bool>();

      // Start the external output window process
      var process = StartOutputWindowProcess(pipeName);

      logger.LogInformation("Waiting for output window connection on pipe: {pipeName}", pipeName);

      // Wait for connection with timeout
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(ConnectionTimeout);

      try
      {
        await pipeServer.WaitForConnectionAsync(cts.Token);
        logger.LogInformation("Output window connected for {project}", projectName);
      }
      catch (OperationCanceledException)
      {
        logger.LogError("Output window connection timeout for {project}", projectName);
        await CleanupConnectionAsync(pipeServer, process);
        throw new TimeoutException($"Output window failed to connect within {ConnectionTimeout.TotalSeconds} seconds");
      }

      // Setup JSON-RPC
      var jsonRpc = ServerBuilder.Build(pipeServer, pipeServer);

      // Handle disconnection
      jsonRpc.Disconnected += (sender, args) =>
      {
        logger.LogInformation("Output window disconnected for {project}", projectName);
        _ = Task.Run(() => StopOutputWindowAsync(dllPath));
      };

      jsonRpc.StartListening();

      var connection = new OutputWindowConnection
      {
        PipeName = pipeName,
        Process = process,
        PipeServer = pipeServer,
        JsonRpc = jsonRpc,
        Connected = connected
      };

      if (!_connections.TryAdd(dllPath, connection))
      {
        logger.LogError("Failed to register output window connection for {project}", projectName);
        await CleanupConnectionAsync(pipeServer, process, jsonRpc);
        throw new InvalidOperationException("Failed to register output window connection");
      }

      connected.SetResult(true);
      logger.LogInformation("Output window ready for {project} on pipe {pipeName}", projectName, pipeName);

      return pipeName;
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to start output window for {project}", projectName);
      throw;
    }
  }

  public async Task StopOutputWindowAsync(string dllPath)
  {
    var projectName = Path.GetFileNameWithoutExtension(dllPath);

    if (!_connections.TryRemove(dllPath, out var connection))
    {
      logger.LogWarning("No output window connection found for {project}", projectName);
      return;
    }

    logger.LogInformation("Stopping output window for {project}", projectName);
    await CleanupConnectionAsync(connection.PipeServer, connection.Process, connection.JsonRpc);
  }

  public async Task SendOutputAsync(string dllPath, DebugOutputEvent output)
  {
    if (!_connections.TryGetValue(dllPath, out var connection))
    {
      return;
    }

    try
    {
      await connection.JsonRpc.InvokeAsync("debugger/output", output);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Failed to send output to external window for {project}",
          Path.GetFileNameWithoutExtension(dllPath));
    }
  }

  public bool IsConnected(string dllPath) => _connections.TryGetValue(dllPath, out var connection)
        && connection.Connected.Task.IsCompletedSuccessfully
        && !connection.Process.HasExited;

  private Process StartOutputWindowProcess(string pipeName)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      UseShellExecute = true,
      CreateNoWindow = false,
      RedirectStandardOutput = false,
      RedirectStandardError = false
    };
    // startInfo.ArgumentList.Add("/home/gus/repo/easy-dotnet-server/EasyDotnet.IDE/bin/Debug/net8.0/EasyDotnet.IDE.dll");
    startInfo.ArgumentList.Add("easydotnet");
    startInfo.ArgumentList.Add("debugger");
    startInfo.ArgumentList.Add("output");
    startInfo.ArgumentList.Add(pipeName);

    var process = Process.Start(startInfo);

    if (process == null)
    {
      throw new InvalidOperationException("Failed to start output window process");
    }

    logger.LogDebug("Started output window process (PID: {pid})", process.Id);
    return process;
  }

  private async Task CleanupConnectionAsync(
      NamedPipeServerStream? pipeServer,
      Process? process,
      JsonRpc? jsonRpc = null)
  {
    if (jsonRpc != null)
    {
      try
      {
        jsonRpc.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error disposing JSON-RPC");
      }
    }

    if (pipeServer != null)
    {
      try
      {
        if (pipeServer.IsConnected)
        {
          pipeServer.Disconnect();
        }
        await pipeServer.DisposeAsync();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error disposing pipe server");
      }
    }

    if (process != null)
    {
      try
      {
        if (!process.HasExited)
        {
          process.Kill();
          await process.WaitForExitAsync();
        }
        process.Dispose();
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Error disposing output window process");
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    var tasks = new List<Task>();

    foreach (var kvp in _connections)
    {
      tasks.Add(StopOutputWindowAsync(kvp.Key));
    }

    await Task.WhenAll(tasks);
    _connections.Clear();
  }
}