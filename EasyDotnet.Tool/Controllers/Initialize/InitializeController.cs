using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EasyDotnet.Notifications;
using EasyDotnet.Services;
using EasyDotnet.Utils;
using StreamJsonRpc;

namespace EasyDotnet.Controllers.Initialize;

public class InitializeController(ClientService clientService) : BaseController
{
  [JsonRpcMethod("initialize")]
  public InitializeResponse Initialize(InitializeRequest request)
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
    clientService.IsInitialized = true;
    clientService.ProjectInfo = request.ProjectInfo;
    clientService.ClientInfo = request.ClientInfo;
    return new InitializeResponse(new ServerInfo("EasyDotnet", serverVersion.ToString()), new ServerCapabilities(GetRpcPaths(), GetRpcNotifications()));
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