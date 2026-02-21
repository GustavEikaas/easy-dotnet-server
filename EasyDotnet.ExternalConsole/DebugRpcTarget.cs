using System.Diagnostics;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using StreamJsonRpc;

namespace EasyDotnet.ExternalConsole;

public sealed class DebugRpcTarget(string hook) : IAsyncDisposable
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

    if (!string.IsNullOrWhiteSpace(request.Cwd))
    {
      psi.WorkingDirectory = request.Cwd;
    }

    if (request.Env != null)
    {
      foreach (var kvp in request.Env)
      {
        psi.Environment[kvp.Key] = kvp.Value;
      }
    }

    var hookPipeName = PipeUtils.GeneratePipeName();
    _hookPipeServer = new NamedPipeServerStream(hookPipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    psi.EnvironmentVariables["DOTNET_STARTUP_HOOKS"] = hook;
    psi.EnvironmentVariables["EASY_DOTNET_HOOK_PIPE"] = hookPipeName;

    var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");

    await _hookPipeServer.WaitForConnectionAsync(ct);

    return new InitializeResponse(process.Id);
  }

  [JsonRpcMethod("resume")]
  public Task ResumeAsync()
  {
    if (_hookPipeServer?.IsConnected == true)
    {
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
    const string pipePrefix = "CoreFxPipe_";
    var pipeName = "EasyDotnet_Ext_Window" + Base64SanitizerRegex().Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "");
    var maxNameLength = MaxPipeNameLength - Path.GetTempPath().Length - pipePrefix.Length - 1;
    return pipeName[..Math.Min(pipeName.Length, maxNameLength)];
  }

  [GeneratedRegex("[/+=]")]
  private static partial Regex Base64SanitizerRegex();
}