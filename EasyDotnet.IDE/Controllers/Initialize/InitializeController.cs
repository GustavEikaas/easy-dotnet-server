using System.Reflection;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Domain.Models.Client;
using EasyDotnet.IDE.BuildHost;
using EasyDotnet.IDE.Notifications;
using EasyDotnet.IDE.Services;
using EasyDotnet.IDE.Utils;
using EasyDotnet.Infrastructure.Editor;
using EasyDotnet.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Initialize;

public class InitializeController(
  IClientService clientService,
  IVisualStudioLocator locator,
  ISolutionService solutionService,
  WorkspaceBuildHostManager workspaceBuildHostManager,
  IMsBuildService msBuildService,
  UpdateCheckerService updateCheckerService,
  IProgressScopeFactory progressScopeFactory,
  ILogger<InitializeController> logger) : BaseController
{
  [JsonRpcMethod("initialize")]
  public async Task<InitializeResponse> Initialize(InitializeRequest request)
  {
    using var progress = progressScopeFactory.Create("Initializing", "Initializing...");
    var assembly = Assembly.GetExecutingAssembly();
    var serverVersion = assembly.GetName().Version ?? throw new NullReferenceException("Server version");

    if (!Version.TryParse(request.ClientInfo.Version, out var clientVersion))
    {
      throw new Exception("Invalid client version format");
    }

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
      PreloadSolutionProjects(clientService.ProjectInfo.SolutionFile);
    }
    clientService.ClientInfo = request.ClientInfo;

    clientService.ClientOptions = request.Options ?? new ClientOptions();
    clientService.UseVisualStudio = clientService.ClientOptions.UseVisualStudio;

    var debuggerOptions = clientService.ClientOptions.DebuggerOptions ?? new DebuggerOptions();
    var binaryPath = debuggerOptions.BinaryPath ?? TryGetNetcoreDbgPath();

    clientService.ClientOptions = clientService.ClientOptions with
    {
      DebuggerOptions = debuggerOptions with { BinaryPath = binaryPath }
    };

    var supportsSingleFileExecution = msBuildService.QuerySdkInstallations().Any(x => x.Version.Major >= 10);
    _ = updateCheckerService.CheckForUpdates(CancellationToken.None);
    progress.Report("Initialized", 100);
    return new InitializeResponse(
        new ServerInfo("EasyDotnet", serverVersion.ToString()),
        new ServerCapabilities(GetRpcPaths(), GetRpcNotifications(), supportsSingleFileExecution),
        new ToolPaths(await TryGetMsBuildPath(locator))
        );
  }

  private void PreloadSolutionProjects(string solutionFile)
  {
    _ = Task.Run(async () =>
        {
          try
          {
            var projects = await solutionService.GetProjectsFromSolutionFile(solutionFile, CancellationToken.None);
            var projectNames = projects.ConvertAll(x => x.ProjectName);
            logger.LogInformation("Preloading {Count} projects:\n{Projects}",
                projectNames.Count,
                string.Join("\n  - ", projectNames.Prepend("")));
            await workspaceBuildHostManager.PreloadProjectsAsync([.. projects.Select(x => x.AbsolutePath)]);

            logger.LogInformation("Finished loading projects:\n{Projects}",
                string.Join("\n  - ", projectNames.Prepend("")));
          }
          catch (Exception e)
          {
            logger.LogError(e, "Failed to preload projects");
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

  private string? TryGetNetcoreDbgPath()
  {
    try
    {
      var debuggerPath = NetCoreDbgLocator.GetNetCoreDbgPath();
      logger.LogInformation("Using bundled netcoredg: {debuggerPath}", debuggerPath);
      return debuggerPath;
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to locate netcoredbg");
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