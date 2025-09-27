using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.Controllers;
using EasyDotnet.Controllers.Initialize;
using EasyDotnet.IDE.Utils;
using EasyDotnet.Notifications;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace EasyDotnet.IDE.Controllers.Initialize;

public class InitializeController(ILogger<InitializeController> logger, IClientService clientService, IVisualStudioLocator locator, IMsBuildService msBuildService) : BaseController
{
  private const string RoslynDllPath = @"C:\Users\Gustav\AppData\Local\nvim-data\mason\packages\roslyn\libexec\Microsoft.CodeAnalysis.LanguageServer.dll";

  [JsonRpcMethod("initialize")]
  public async Task<InitializeResponse> Initialize(InitializeRequest request)
  {
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
    _ = Task.Run(async () =>
    {
      var proxy = new RoslynProxy("EasyDotnet_ClientPipe", RoslynDllPath, logger);
      await proxy.StartAsync();
    });
    clientService.IsInitialized = true;
    clientService.ProjectInfo = request.ProjectInfo;
    clientService.ClientInfo = request.ClientInfo;

    if (request.Options is not null)
    {
      clientService.UseVisualStudio = request.Options.UseVisualStudio;
      clientService.ClientOptions = request.Options;
    }

    var supportsSingleFileExecution = msBuildService.QuerySdkInstallations().Any(x => x.Version.Major >= 10);

    return new InitializeResponse(
        new ServerInfo("EasyDotnet", serverVersion.ToString()),
        new ServerCapabilities(GetRpcPaths(), GetRpcNotifications(), supportsSingleFileExecution),
        new ToolPaths(await TryGetMsBuildPath(locator))
        );
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

  private static List<string> GetRpcPaths() =>
      [.. AssemblyScanner.GetControllerTypes()
          .SelectMany(rpcType =>
              rpcType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                  .Where(m => m.GetCustomAttribute<JsonRpcMethodAttribute>() is not null)
                  .Select(m => m.GetCustomAttribute<JsonRpcMethodAttribute>()!.Name)
          )];

  private static List<string> GetRpcNotifications() =>
      [.. AssemblyScanner.GetNotificationDispatchers()
          .SelectMany(rpcType =>
              rpcType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                  .Where(m => m.GetCustomAttribute<RpcNotificationAttribute>() is not null)
                  .Select(m => m.GetCustomAttribute<RpcNotificationAttribute>()!.Name)
          )];
}