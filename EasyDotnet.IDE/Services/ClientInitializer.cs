using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Project.Services;
using EasyDotnet.IDE.Sdk;
using Microsoft.Extensions.Logging;

namespace EasyDotnet.IDE.Services;

/// <summary>
/// Applies a client's workspace and options to <see cref="IClientService"/>. Shared by the
/// <c>initialize</c> RPC and hosts that start without an editor handshake.
/// </summary>
public class ClientInitializer(
  IClientService clientService,
  ProjectGraphService projectGraphService,
  SdkService sdkService,
  INotificationService notificationService,
  ILogger<ClientInitializer> logger)
{
  public void Initialize(ProjectInfo projectInfo, ClientInfo clientInfo, ClientOptions? options)
  {
    Directory.SetCurrentDirectory(projectInfo.RootDir);
    clientService.IsInitialized = true;
    clientService.ProjectInfo = projectInfo;
    if (projectInfo.SolutionFile is not null)
    {
      LoadSolutionProjects(projectInfo.SolutionFile);
    }
    clientService.ClientInfo = clientInfo;

    clientService.ClientOptions = options ?? new ClientOptions();
    clientService.UseVisualStudio = clientService.ClientOptions.UseVisualStudio;

    var debuggerOptions = clientService.ClientOptions.DebuggerOptions ?? new DebuggerOptions();
    var engine = DebuggerLocator.GetConfiguredEngine(debuggerOptions.Engine, debuggerOptions.BinaryPath);
    var binaryPath = debuggerOptions.BinaryPath ?? TryGetDebuggerPath(debuggerOptions.Engine);

    clientService.ClientOptions = clientService.ClientOptions with
    {
      DebuggerOptions = debuggerOptions with { BinaryPath = binaryPath, Engine = DebuggerLocator.GetEngineName(engine) }
    };

    clientService.SupportsSingleFileExecution = sdkService.QuerySdkInstallations().Any(x => x.Version.Major >= 10);
  }

  private void LoadSolutionProjects(string solutionFile)
  {
    _ = Task.Run(async () =>
        {
          try
          {
            await projectGraphService.LoadSolutionAsync(solutionFile, CancellationToken.None);
            await notificationService.NotifySolutionProjectsLoaded();
          }
          catch (Exception e)
          {
            logger.LogError(e, "Failed to load solution projects");
          }

        });
  }

  private string? TryGetDebuggerPath(string? engineName = null)
  {
    try
    {
      var debugger = DebuggerLocator.ResolveDebugger(engineName);
      logger.LogInformation(
          "Using {debuggerEngine} debugger from {debuggerSource}: {debuggerPath}",
          DebuggerLocator.GetEngineName(debugger.Engine),
          debugger.Source,
          debugger.Path);
      return debugger.Path;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to locate debugger");
    }
    return null;
  }
}