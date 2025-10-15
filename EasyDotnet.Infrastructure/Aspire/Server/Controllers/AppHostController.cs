using StreamJsonRpc;

namespace EasyDotnet.Infrastructure.Aspire.Server.Controllers;

public class AppHostController
{
  [JsonRpcMethod("launchAppHost")]
  public void LaunchAppHost(string token, string projectFile, List<string> arguments, List<EnvVar> environment, bool debug)
  {
    Console.WriteLine($"[{token}] Launching AppHost: {projectFile}");
    Console.WriteLine($"Debug: {debug}");
    Console.WriteLine("Arguments:");
    foreach (var arg in arguments)
    {
      Console.WriteLine($"  {arg}");
    }

    Console.WriteLine("Environment variables:");
    foreach (var envVar in environment)
    {
      Console.WriteLine($"  {envVar.Name}={envVar.Value}");
    }
  }

  [JsonRpcMethod("notifyAppHostStartupCompleted")]
  public void NotifyAppHostStartupCompleted(string token)
  {
    Console.WriteLine($"[{token}] AppHost startup completed");
  }

}

public record EnvVar(string Name, string Value);