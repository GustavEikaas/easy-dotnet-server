using System.IO.Pipes;
using EasyDotnet.Application.Interfaces;
using EasyDotnet.IDE.DebuggerStrategies;
using EasyDotnet.IDE.Utils;

namespace EasyDotnet.IDE.StartupHook;


public class StartupHookService() : IStartupHookService
{
  public StartupHookSession CreateSession(Dictionary<string, string>? baseEnv = null)
  {
    var hookPath = StartupHookLocator.GetStartupHookPath();
    var pipeName = PipeUtils.GeneratePipeName();
    var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    var env = BuildEnv(baseEnv, hookPath, pipeName);
    return new StartupHookSession(pipe, env);
  }

  private static Dictionary<string, string> BuildEnv(
      Dictionary<string, string>? baseEnv,
      string hookPath,
      string pipeName)
  {
    var env = new Dictionary<string, string>(
        baseEnv ?? [], StringComparer.OrdinalIgnoreCase);

    env["DOTNET_STARTUP_HOOKS"] = env.TryGetValue("DOTNET_STARTUP_HOOKS", out var existing)
        && !string.IsNullOrWhiteSpace(existing)
            ? $"{hookPath}{Path.PathSeparator}{existing}"
            : hookPath;

    env["EASY_DOTNET_HOOK_PIPE"] = pipeName;
    return env;
  }
}

