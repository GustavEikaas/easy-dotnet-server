using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using Spectre.Console.Cli;
using StreamJsonRpc;

namespace EasyDotnet.ExternalConsole.Commands;

public sealed class DebugCommand : AsyncCommand<DebugCommand.Settings>
{
  public sealed class Settings : CommandSettings
  {
    [Description("The pipe to connect to")]
    [CommandOption("--pipe")]
    public string Pipe { get; init; } = "";
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
  {
    await using var pipeClient = new NamedPipeClientStream(".", settings.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipeClient.ConnectAsync(cancellationToken);

    using var rpc = new JsonRpc(pipeClient);

    rpc.TraceSource.Listeners.Add(new ConsoleTraceListener());
    rpc.TraceSource.Switch.Level = SourceLevels.Verbose;

    rpc.AddLocalRpcTarget(new DebugRpcTarget());
    rpc.StartListening();

    await rpc.Completion;
    return 0;
  }
}