using System.Reflection;
using EasyDotnet.Controllers;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Editor;
using EasyDotnet.IDE.Interfaces;
using EasyDotnet.IDE.Models.Client;
using EasyDotnet.IDE.Notifications;
using EasyDotnet.IDE.Project.Services;
using EasyDotnet.IDE.Sdk;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Settings;
using EasyDotnet.IDE.Utils;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Initialize;

public class InitializeController(
  IClientService clientService,
  IVisualStudioLocator locator,
  ProjectGraphService projectGraphService,
  SdkService sdkService,
  INotificationService notificationService,
  UpdateCheckerService updateCheckerService,
  IProgressScopeFactory progressScopeFactory,
  SettingsService settingsService,
  ILogger<InitializeController> logger) : BaseController
{
  [JsonRpcMethod("initialize")]
  public async Task<InitializeResponse> Initialize(InitializeRequest request)
  {
    using var progress = progressScopeFactory.Create("Initializing", "Initializing...");
    var assembly = Assembly.GetExecutingAssembly();
    var serverVersion = assembly.GetName().Version ?? throw new NullReferenceException("Server version");

    var clientVersion = request.ClientInfo.Version;

    if (clientVersion.Major != serverVersion.Major)
    {
      if (clientVersion.Major < serverVersion.Major)
      {
        throw new Exception($"Client is outdated. Please update your client. Server Version: {serverVersion}, Client Version: {clientVersion}");
      }
      else
      {
        throw new Exception($"Server is outdated. Please update the server. `dotnet tool install -g EasyDotnet` Server Version: {serverVersion}, Client Version: {clientVersion}");
      }
    }
    Directory.SetCurrentDirectory(request.ProjectInfo.RootDir);
    clientService.IsInitialized = true;
    clientService.ProjectInfo = request.ProjectInfo;
    if (clientService.ProjectInfo.SolutionFile is not null)
    {
      LoadSolutionProjects(clientService.ProjectInfo.SolutionFile);
    }
    clientService.ClientInfo = request.ClientInfo;

    clientService.ClientOptions = request.Options ?? new ClientOptions();
    clientService.UseVisualStudio = clientService.ClientOptions.UseVisualStudio;

    var debuggerOptions = clientService.ClientOptions.DebuggerOptions ?? new DebuggerOptions();
    var engine = DebuggerLocator.GetConfiguredEngine(debuggerOptions.Engine, debuggerOptions.BinaryPath);
    var binaryPath = debuggerOptions.BinaryPath ?? TryGetDebuggerPath(debuggerOptions.Engine);

    clientService.ClientOptions = clientService.ClientOptions with
    {
      DebuggerOptions = debuggerOptions with { BinaryPath = binaryPath, Engine = DebuggerLocator.GetEngineName(engine) }
    };

    var supportsSingleFileExecution = sdkService.QuerySdkInstallations().Any(x => x.Version.Major >= 10);
    clientService.SupportsSingleFileExecution = supportsSingleFileExecution;
    _ = updateCheckerService.CheckForUpdates(CancellationToken.None);
    _ = settingsService.PushActiveProjectChangedAsync();
    progress.Report("Initialized", 100);
    return new InitializeResponse(
        new ServerInfo("EasyDotnet", serverVersion.ToString()),
        new ServerCapabilities(GetRpcPaths(), GetRpcNotifications(), supportsSingleFileExecution),
        new ToolPaths(await TryGetMsBuildPath(locator))
        );
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

  private static async Task<string?> TryGetMsBuildPath(IVisualStudioLocator locator)
  {
    try
    {
      return await locator.GetVisualStudioMSBuildPath();
    }
    catch
    {
      return null;
    }
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

  private static List<string> GetRpcPaths() => AssemblyScanner.GetControllerTypes()
          .SelectMany(rpcType =>
              rpcType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                  .Where(m => m.GetCustomAttribute<JsonRpcMethodAttribute>() is not null)
                  .Select(m => m.GetCustomAttribute<JsonRpcMethodAttribute>()!.Name)!
          ).ToList()!;

  private static List<string> GetRpcNotifications() =>
      [.. AssemblyScanner.GetNotificationDispatchers()
          .SelectMany(rpcType =>
              rpcType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                  .Where(m => m.GetCustomAttribute<RpcNotificationAttribute>() is not null)
                  .Select(m => m.GetCustomAttribute<RpcNotificationAttribute>()!.Name)
          )];
}