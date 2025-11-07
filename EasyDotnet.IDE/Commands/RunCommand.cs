using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;

namespace EasyDotnet.IDE.Commands;

public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [CommandOption("--logLevel <LEVEL>")]
    [Description("Logging verbosity (Off, Critical, Error, Warning, Information, Verbose, All). Default: Off")]
    [DefaultValue(SourceLevels.Off)]
    public SourceLevels LogLevel { get; init; } = SourceLevels.Off;
  }
  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    await StartServerAsync(settings.LogLevel);
    return 0;
  }

  private static async Task StartServerAsync(SourceLevels logLevel)
  {
    var pipeName = GeneratePipeName();

    var clientId = 0;
    while (true)
    {
      var stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
      Console.WriteLine($"Named pipe server started: {pipeName}");
      await stream.WaitForConnectionAsync();
      _ = RespondToRpcRequestsAsync(stream, ++clientId, logLevel);
    }
  }

  private static string GeneratePipeName()
  {
#if DEBUG 
    var pipe = "EasyDotnet_ROcrjwn9kiox3tKvRWcQg";
    return pipe;
#else
    var pipe = EasyDotnet.IDE.Utils.PipeUtils.GeneratePipeName();
    return pipe;
#endif
  }

  private static async Task RespondToRpcRequestsAsync(Stream stream, int clientId, SourceLevels logLevel)
  {
    var rpc = JsonRpcServerBuilder.Build(stream, stream, null, logLevel);
    rpc.StartListening();
    await rpc.Completion;
    await Console.Error.WriteLineAsync($"Connection #{clientId} terminated.");
  }
}