using System;
using System.Diagnostics;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.Infrastructure.Aspire.Server;
using EasyDotnet.Infrastructure.Aspire.Server.Controllers;
using EasyDotnet.Infrastructure.Dap;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.NetCoreDbg;

public sealed record DebuggerStartRequest(
  string TargetPath,
  string? TargetFramework,
  string? Configuration,
  string? LaunchProfileName
);

public sealed record DebuggerStartResponse(bool Success, int Port);

public class NetCoreDbgController(IMsBuildService msBuildService, ILaunchProfileService launchProfileService, INetcoreDbgService netcoreDbgService, IClientService clientService, ILogger<NetCoreDbgController> logger) : BaseController
{
  [JsonRpcMethod("debugger/start")]
  public async Task<DebuggerStartResponse> StartDebugger(DebuggerStartRequest request, CancellationToken cancellationToken)
  {

    var project = await msBuildService.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.Configuration ?? "Debug", cancellationToken);

    if (project.IsAspireHost)
    {
      logger.LogInformation($"Starting debugging of aspire apphost {request.TargetPath}");

      var cert = AspireServer.GenerateSslCert();
      logger.LogInformation("Generated TLS certificate for Aspire server");
      var (listener, endpoint) = AspireServer.CreateListener();
      logger.LogInformation("Aspire TCP listener started at {Endpoint}", endpoint);
      var token = Guid.NewGuid().ToString("N");
      logger.LogInformation("Generated session token {Token}", token);

      //TODO: process must be managed and released at some point
      var process = AspireServer.StartAspireHost(endpoint, token, cert);
      logger.LogInformation("Started Aspire CLI process with PID {Pid}", process.Id);
      logger.LogInformation("Waiting for Aspire CLI to connect...");
      var client = await listener.AcceptTcpClientAsync(cancellationToken);
      logger.LogInformation("Aspire CLI connected from {RemoteEndPoint}", client.Client.RemoteEndPoint);

      var ssl = new SslStream(client.GetStream(), false);
      await ssl.AuthenticateAsServerAsync(
          cert,
          clientCertificateRequired: false,
          enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12,
          checkCertificateRevocation: false
      );
      logger.LogInformation("SSL authentication completed with Aspire CLI");

      var rpcServer = AspireServer.CreateAspireServer(ssl);
      var controller = new DebuggingController();
      rpcServer.AddLocalRpcTarget(controller);

      var debugSessionId = await rpcServer.InvokeAsync<string?>("getDebugSessionId");

      var sessionCompletionTcs = new TaskCompletionSource<bool>();
      var portTcs = new TaskCompletionSource<int>();


      controller.OnDebugSessionStarted = async (workingDir, projectFile, debug) =>
       {
         try
         {
           logger.LogInformation("[Aspire] StartDebugSession called for project {ProjectFile} in {WorkingDir}", projectFile, workingDir);

           var project = await msBuildService.GetOrSetProjectPropertiesAsync(projectFile!, null, "Debug", cancellationToken);
           logger.LogInformation("[Aspire] Retrieved MSBuild properties for {ProjectFile}", projectFile);

           var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
           if (string.IsNullOrEmpty(binaryPath))
             throw new Exception("Failed to start debugger, no binary path provided");

           logger.LogInformation("[Aspire] Starting NetcoreDbg debugger process for {ProjectFile}", projectFile);
           var port = await netcoreDbgService.Start(binaryPath, project, projectFile!, null, null);
           logger.LogInformation("[Aspire] Debugger started on port {Port}", port);

           portTcs.SetResult(port); // notify Neovim client

           logger.LogInformation("[Aspire] Waiting for DebuggerProxy to complete...");
           await netcoreDbgService.Completion;

           logger.LogInformation("[Aspire] DebuggerProxy completed, signaling Aspire RPC completion");
           controller.CompleteDebugSession();
         }
         catch (Exception ex)
         {
           logger.LogError(ex, "Error during Aspire DebugSession start");
           portTcs.TrySetException(ex);
           controller.CompleteDebugSession();
         }
       };

      rpcServer.StartListening();
      logger.LogInformation("[Aspire] JSON-RPC server listening");

      // Await the TCS in background to keep Aspire RPC open
      _ = sessionCompletionTcs.Task.ContinueWith(_ =>
                {
                  try
                  {
                    logger.LogInformation("[Aspire] Cleaning up Aspire session resources...");
                    ssl.Dispose();
                    client.Close();
                    listener.Stop();
                    if (!process.HasExited) process.Kill();
                    rpcServer.Dispose();
                    logger.LogInformation("[Aspire] Cleanup complete");
                  }
                  catch (Exception ex)
                  {
                    logger.LogWarning(ex, "[Aspire] Error during cleanup");
                  }
                }, cancellationToken);

      var debuggerPort = await portTcs.Task;
      logger.LogInformation("Returning debugger port {Port} to Neovim client", debuggerPort);
      return new DebuggerStartResponse(true, debuggerPort);
    }
    else
    {
      var launchProfile = !string.IsNullOrEmpty(request.LaunchProfileName)
          ? (launchProfileService.GetLaunchProfiles(request.TargetPath)
             is { } profiles && profiles.TryGetValue(request.LaunchProfileName, out var profile)
                ? profile
                : null)
          : null;

      var binaryPath = clientService.ClientOptions?.DebuggerOptions?.BinaryPath;
      if (string.IsNullOrEmpty(binaryPath))
      {
        throw new Exception("Failed to start debugger, no binary path provided");
      }

      var res = StartVsTestIfApplicable(project, request.TargetPath);

      var port = await netcoreDbgService.Start(binaryPath, project, request.TargetPath, launchProfile, res);
      return new DebuggerStartResponse(true, port);
    }

  }

  private static (Process, int)? StartVsTestIfApplicable(DotnetProject project, string projectPath) => project.IsTestProject && !project.TestingPlatformDotnetTestSupport ? VsTestHelper.StartTestProcess(projectPath) : null;
}