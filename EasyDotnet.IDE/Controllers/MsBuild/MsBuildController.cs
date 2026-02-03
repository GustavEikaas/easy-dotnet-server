using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.MsBuild;
using EasyDotnet.Domain.Models.MsBuild.Build;
using EasyDotnet.Domain.Models.MsBuild.Project;
using EasyDotnet.IDE.BuildServerContracts;
using EasyDotnet.IDE.Controllers.BuildServer;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.MsBuild;

public class MsBuildController(IClientService clientService, IMsBuildService msBuild, ILoggerFactory loggerFactory) : BaseController
{
  [JsonRpcMethod("msbuild/build")]
  public async Task<BuildResultResponse> Build(BuildRequest request)
  {
    clientService.ThrowIfNotInitialized();

    // 1. LOCATE THE SERVER DLL
    // We need the absolute path. Adjust this relative path logic if your IDE 
    // runs from a different working directory than the repo root.
    const string serverDllPath = "C:/Users/gusta/repo/easy-dotnet-server/EasyDotnet.BuildServer/bin/Debug/net8.0/EasyDotnet.BuildServer.dll";

    if (!File.Exists(serverDllPath))
    {
      throw new FileNotFoundException($"Could not find Build Server at: {serverDllPath}");
    }

    // 2. SPAWN THE PROCESS
    var startInfo = new ProcessStartInfo
    {
      FileName = "dotnet",
      Arguments = $"exec \"{serverDllPath}\"", // "exec" runs the dll
      RedirectStandardInput = true,  // We write to this (RPC Request)
      RedirectStandardOutput = true, // We read from this (RPC Response)
      RedirectStandardError = true,  // We read logs from here
      UseShellExecute = false,
      CreateNoWindow = true,
      // Environment Variables to force the Server (running on 8 or 10) 
      // to roll forward to the highest installed SDK
      Environment = { ["DOTNET_ROLL_FORWARD"] = "LatestMajor" }
    };

    using var serverProcess = new Process { StartInfo = startInfo };

    // Capture Stderr for debugging (so you can see "BuildServer running on..." logs)
    serverProcess.ErrorDataReceived += (s, e) =>
    {
      if (e.Data != null) Console.WriteLine($"[BuildServer-Log] {e.Data}");
    };

    serverProcess.Start();
    serverProcess.BeginErrorReadLine(); // Start reading logs

    try
    {
      // 3. ESTABLISH RPC CONNECTION
      // We attach to the process's StdOut (Read) and StdIn (Write)
      var logController = new BuildServerController(loggerFactory.CreateLogger<BuildServerController>());
      var rpc = JsonRpc.Attach(
          serverProcess.StandardInput.BaseStream,
          serverProcess.StandardOutput.BaseStream,
          target: logController
      );

      // 4. SEND THE REQUEST
      // We call the method "build" which we defined in BuildService.cs
      var serverRequest = new BuildServerRequest(request.TargetPath, request.Configuration ?? "Debug");

      Console.WriteLine($"Sending Build Request for: {request.TargetPath}");

      var result = await rpc.InvokeWithParameterObjectAsync<BuildServerResult>("build", serverRequest);

      // 5. MAP RESPONSE BACK TO IDE FORMAT
      return new BuildResultResponse(
          result.Success,
          MapMessages(result.Errors).AsAsyncEnumerable(),
          MapMessages(result.Warnings).AsAsyncEnumerable()
      );
    }
    catch (Exception ex)
    {
      // If the server crashes or path is wrong, you'll see it here
      Console.WriteLine($"RPC Failed: {ex}");
      throw;
    }
    finally
    {
      // For this test, kill the process when done.
      if (!serverProcess.HasExited)
      {
        serverProcess.Kill();
      }
    }
  }

  private IEnumerable<BuildMessageWithProject> MapMessages(List<BuildMessageDto> messages) => messages.Select(m => new BuildMessageWithProject(
                                                                                                   m.Type, m.FilePath, m.LineNumber, m.ColumnNumber, m.Code, m.Message, m.ProjectFile
                                                                                               ));

  [JsonRpcMethod("msbuild/project-properties")]
  public async Task<DotnetProjectV1> QueryProjectProperties(ProjectPropertiesRequest request)
  {
    clientService.ThrowIfNotInitialized();
    var project = await msBuild.GetOrSetProjectPropertiesAsync(request.TargetPath, request.TargetFramework, request.ConfigurationOrDefault);

    return project.ToResponse(await msBuild.BuildRunCommand(project), await msBuild.BuildBuildCommand(project), await msBuild.BuildTestCommand(project));
  }

  [JsonRpcMethod("msbuild/list-project-reference")]
  public async Task<List<string>> GetProjectReferences(string projectPath)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.GetProjectReferencesAsync(Path.GetFullPath(projectPath));
  }

  [JsonRpcMethod("msbuild/list-package-reference")]
  public async Task<IAsyncEnumerable<PackageReference>> GetPackageReferences(string projectPath, string targetFramework, CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    var packages = await msBuild.GetPackageReferencesAsync(Path.GetFullPath(projectPath), targetFramework, cancellationToken);
    return packages.ToBatchedAsyncEnumerable(50);
  }

  [JsonRpcMethod("msbuild/add-project-reference")]
  public async Task<bool> AddProjectReference(string projectPath, string targetPath, CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.AddProjectReferenceAsync(Path.GetFullPath(projectPath), Path.GetFullPath(targetPath), cancellationToken);
  }

  [JsonRpcMethod("msbuild/remove-project-reference")]
  public async Task<bool> RemoveProjectReference(string projectPath, string targetPath, CancellationToken cancellationToken)
  {
    clientService.ThrowIfNotInitialized();
    return await msBuild.RemoveProjectReferenceAsync(Path.GetFullPath(projectPath), targetPath, cancellationToken);
  }
}