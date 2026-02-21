using System.Diagnostics;
using StreamJsonRpc;

namespace EasyDotnet.ExternalConsole;

public sealed class DebugRpcTarget
{
  [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
  public async Task<InitializeResponse> InitializeAsync(InitializeRequest request, CancellationToken ct)
  {
    var psi = new ProcessStartInfo { FileName = request.Program };
    foreach (var arg in request.Args)
      psi.ArgumentList.Add(arg);

    psi.EnvironmentVariables["DOTNET_DefaultDiagnosticPortSuspend"] = "1";

    var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");

    //TODO: if process is killed, we do a reverse request to client to inform that we need to dispose the debugsession 

    return new InitializeResponse(process.Id);
  }
}