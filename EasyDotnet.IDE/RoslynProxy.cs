using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace EasyDotnet.IDE;

public class RoslynProxy(string clientPipeName, string roslynDllPath, ILogger logger, IClientService clientService, INotificationService notificationService)
{

  private static JsonMessageFormatter CreateJsonMessageFormatter() => new()
  {
    JsonSerializer = { ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }}
  };
  private NamedPipeServerStream? _clientPipe;
  private JsonRpc? _clientRpc;
  private JsonRpc? _roslynRpc;

  public async Task StartAsync()
  {
    logger.LogInformation("Waiting for EasyDotnet client...");
    _clientPipe = new NamedPipeServerStream(clientPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    await _clientPipe.WaitForConnectionAsync();
    logger.LogInformation("Client connected");
    var roslynLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyDotnet", "RoslynLogs");
    logger.LogInformation("Logging to {dir}", roslynLogDir);
    Directory.CreateDirectory(roslynLogDir);
    var roslynProcess = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = "dotnet",
        Arguments = $"\"{roslynDllPath}\" --stdio --logLevel=Information --extensionLogDirectory=\"{roslynLogDir}\"",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      },
      EnableRaisingEvents = true
    };
    // Capture stderr
    roslynProcess.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        logger.LogError("[Roslyn-STDERR] {Message}", e.Data);
    };
    roslynProcess.Start();
    roslynProcess.BeginErrorReadLine(); // optional logging
    roslynProcess.ErrorDataReceived += (s, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
        logger.LogError("[Roslyn LSP] {Message}", e.Data);
    };

    // Attach JSON-RPC
    //
    var clientHandler = new HeaderDelimitedMessageHandler(_clientPipe, _clientPipe, CreateJsonMessageFormatter());
    _clientRpc = new JsonRpc(clientHandler);

    var handler = new HeaderDelimitedMessageHandler(roslynProcess.StandardInput.BaseStream, roslynProcess.StandardOutput.BaseStream, CreateJsonMessageFormatter());
    _roslynRpc = new JsonRpc(handler);

    _clientRpc.AddRemoteRpcTarget(_roslynRpc);
    _roslynRpc.AddRemoteRpcTarget(_clientRpc);
    // var sln = Path.GetFullPath(clientService.ProjectInfo!.SolutionFile!);
    var sln = clientService.ProjectInfo!.SolutionFile!;
    _clientRpc.AddLocalRpcTarget(new ClientInitializedHandler(_roslynRpc, sln, logger));
    _roslynRpc.AddLocalRpcTarget(new RoslynNotificationInterceptor(_clientRpc, logger, notificationService));

    _roslynRpc.TraceSource.Switch.Level = SourceLevels.Verbose;
    _clientRpc.TraceSource.Switch.Level = SourceLevels.Verbose;
    // var serverToClientRpc = new JsonRpc(clientStream, new ClientTarget());
    // var serverToRemoteRpc = new JsonRpc(remoteStream, new RemoteTarget());
    //
    // // Forward all messages from client to Roslyn
    // serverToClientRpc.AddRemoteTarget(serverToRemoteRpc);
    //
    // // Forward all messages from Roslyn to client
    // serverToRemoteRpc.AddRemoteTarget(serverToClientRpc);
    // Forward messages client -> Roslyn
    // _clientRpc.AddLocalRpcTarget(new ProxyForwarder(_roslynRpc));
    // // Forward messages Roslyn -> client
    // _roslynRpc.AddLocalRpcTarget(new ProxyForwarder(_clientRpc));

    _roslynRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger, "roslyn"));
    _clientRpc.TraceSource.Listeners.Add(new JsonRpcLogger(logger, "neovim-client"));
    _clientRpc.StartListening();
    _roslynRpc.StartListening();
    logger.LogInformation("Roslyn proxy attached and forwarding messages");
  }
}

public sealed record SolutionOpenNotification(string Solution);

public class ClientInitializedHandler(JsonRpc roslynRpc, string solutionPath, ILogger logger)
{
  [JsonRpcMethod("initialized")]
  public async Task OnClientInitialized()
  {
    logger.LogInformation("Client initialized, sending solution/open to Roslyn: {solution}", solutionPath);
    await roslynRpc.NotifyWithParameterObjectAsync("solution/open", new SolutionOpenNotification("file:///C:/Users/gusta/repo/easy-dotnet-server-test/EasyDotnet.sln"));
  }
}

public class RoslynNotificationInterceptor(JsonRpc clientRpc, ILogger logger, INotificationService notificationService)
{
  [JsonRpcMethod("workspace/projectInitializationComplete")]
  public async Task OnProjectInitializationComplete()
  {
    logger.LogInformation("Roslyn finished loading solution/project.");

    // Forward to client
    await notificationService.LspReady();
    await clientRpc.NotifyAsync("workspace/projectInitializationComplete", new { });

    // Additional server-side logic here
  }
}