using System.Diagnostics;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using StreamJsonRpc;

namespace EasyDotnet.ExternalConsole;

public sealed class DebugRpcTarget : IAsyncDisposable
{
  private NamedPipeServerStream? _hookPipeServer;

  [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
  public async Task<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken ct)
  {
    var psi = new ProcessStartInfo { FileName = request.Program };
    foreach (var arg in request.Args)
    {
      psi.ArgumentList.Add(arg);
    }

    var hookPipeName = PipeUtils.GeneratePipeName();
    _hookPipeServer = new NamedPipeServerStream(hookPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    var file = "C:/Users/gusta/repo/easy-dotnet-server/EasyDotnet.StartupHook/bin/Debug/net8.0/EasyDotnet.StartupHook.dll";

    psi.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = file;
    psi.EnvironmentVariables["EASY_DOTNET_HOOK_PIPE"] = hookPipeName;

    // 2. Start the process
    var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");

    // 3. Wait for the hook to connect. Once it connects, we know 100% 
    // that the target app is frozen securely inside StartupHook.Initialize()
    await _hookPipeServer.WaitForConnectionAsync(ct);

    // 4. Return the PID back to the IDE so it can attach netcoredbg
    return new InitializeResponse(process.Id);
  }

  [JsonRpcMethod("resume")]
  public Task ResumeAsync()
  {
    if (_hookPipeServer?.IsConnected == true)
    {
      // Write a single byte to the pipe. 
      // The Hook's `client.ReadByte()` will unblock, and the app will start!
      _hookPipeServer.WriteByte(1);
      _hookPipeServer.Flush();
    }
    return Task.CompletedTask;
  }

  public async ValueTask DisposeAsync()
  {
    if (_hookPipeServer != null)
    {
      await _hookPipeServer.DisposeAsync();
    }
  }
}


public static partial class PipeUtils
{
  private const int MaxPipeNameLength = 104;
  public static string GeneratePipeName()
  {
    var pipePrefix = "CoreFxPipe_";
    var pipeName = "EasyDotnet_" + Base64SanitizerRegex().Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "");
    var maxNameLength = MaxPipeNameLength - Path.GetTempPath().Length - pipePrefix.Length - 1;
    return pipeName[..Math.Min(pipeName.Length, maxNameLength)];
  }

  [GeneratedRegex("[/+=]")]
  private static partial Regex Base64SanitizerRegex();
}